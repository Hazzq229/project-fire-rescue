using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public class WalkingPresentationTests
{
    const string Scene = "Assets/Scenes/Gameloop_Demo.unity";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static Component C(GameObject go, string name) => go.GetComponent(T(name));
    static object P(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethods(Flags)
        .First(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v)).Invoke(value, args);
    static object HoseCheck(string name, params object[] args) => typeof(CooperativeHoseTests)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    static void Keys(params string[] keys) => HoseCheck("KeyboardState", new object[] { keys });
    static IEnumerator Wait(float seconds) { for (float until = Time.time + seconds; Time.time < until;) yield return null; }
    static void Position(GameObject go, Vector3 position, Quaternion rotation)
    {
        go.transform.SetPositionAndRotation(position, rotation);
        var body = go.GetComponent<Rigidbody>(); body.position = position; body.rotation = rotation;
        if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        Physics.SyncTransforms();
    }
    static IEnumerator Ready(bool useSceneCamera = false)
    {
        HoseCheck("ResetKeyboard");
        var adapter = C(GameObject.Find("FR_Level_1"), "TA.AdaptiveAnimation.AdaptiveSceneProps");
        for (float until = Time.time + 3f; !(bool)P(adapter, "IsReady") && Time.time < until;) yield return null;
        Assert.IsTrue((bool)P(adapter, "IsReady"));
        if (!useSceneCamera)
        {
            // Gait fixtures measure known world-space headings and foot positions.
            foreach (var name in new[] { "Player1", "Player2" })
            {
                var mover = C(GameObject.Find(name), "ThirdPersonPlayerController");
                mover.GetType().GetProperty("UseCameraRelativeMovement").SetValue(mover, false);
            }
        }
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "WalkingPresentationFloor";
        floor.transform.position = new Vector3(30f, -.1f, 30f);
        floor.transform.localScale = new Vector3(30f, .2f, 30f);
        Physics.SyncTransforms();
        yield return Wait(.2f);
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying)
        {
            HoseCheck("ResetKeyboard");
            yield return new ExitPlayMode();
        }
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
    }

    [UnityTest]
    public IEnumerator EachPlayerWalksAndDashesInTheirOwnCameraDirection()
    {
        EditorSceneManager.OpenScene(Scene);
        yield return new EnterPlayMode();
        yield return VerifyCameraRelativeMovement();
    }

    static IEnumerator VerifyCameraRelativeMovement()
    {
        yield return Ready(true);
        foreach (var (name, walkKey, dashKey, heading, startX) in new[]
        {
            ("Player1", "W", "LeftShift", Vector3.right, 32f),
            ("Player2", "UpArrow", "RightAlt", Vector3.left, 28f)
        })
        {
            var player = GameObject.Find(name);
            var mover = C(player, "ThirdPersonPlayerController");
            var start = new Vector3(startX, .35f, 30f);
            Assert.IsTrue((bool)P(mover, "UseCameraRelativeMovement"), "Camera-relative movement must be enabled by default");
            var view = new GameObject(player.name + " Test View").transform;
            view.rotation = Quaternion.LookRotation(heading + Vector3.down * .6f);
            mover.GetType().GetProperty("CameraTransform").SetValue(mover, view);
            Position(player, start, Quaternion.LookRotation(heading));
            yield return Wait(.2f);

            Keys(walkKey);
            yield return WaitForMoveInput(mover, Vector2.up);
            yield return Wait(.35f);
            Vector3 dashStart = player.transform.position;
            Assert.Greater(Vector3.Dot(dashStart - start, heading), .3f, player.name + " camera-relative walk");

            // Turn the view away from the current facing to distinguish input from a forward dash.
            view.rotation = Quaternion.LookRotation(-heading + Vector3.down * .6f);
            Keys(walkKey, dashKey);
            yield return Wait(.14f);
            Keys();
            Assert.IsTrue(player.GetComponent<Animator>().GetBool("isDashing"), player.name + " keeps the existing dash action");
            Vector3 delta = player.transform.position - dashStart;
            Assert.Greater(Vector3.Dot(delta, -heading), .5f, player.name + " camera-relative dash");
            Assert.Less(Mathf.Abs(delta.z), .06f, "Dash should travel along the same camera axes as walking");
        }
    }

    [UnityTest]
    public IEnumerator BothPlayersKeepSoleContactDuringStraightWalkAndTurns()
    {
        EditorSceneManager.OpenScene(Scene);
        yield return new EnterPlayMode();
        yield return VerifySoleContact();
    }

    static IEnumerator VerifySoleContact()
    {
        yield return Ready();
        // The tiles at the authored spawn are above the terrain collider.
        const float tileHeight = .20035f;
        const float nearFloor = tileHeight + .024f;
        var players = new[] { GameObject.Find("Player1"), GameObject.Find("Player2") };
        var movers = players.Select(p => C(p, "ThirdPersonPlayerController")).ToArray();
        var shoes = players.Select(p => p.GetComponentsInChildren<SkinnedMeshRenderer>()
            .First(r => r.enabled && r.name == "Sepatu")).ToArray();
        var baked = new Mesh();
        int captureRate = Time.captureFramerate;
        Time.captureFramerate = 50;
        try
        {
            var unsupportedPivots = new List<string>();
            foreach (var (angle, leadIn) in new[] { (0, .52f), (90, .52f), (180, .52f), (180, .59f), (180, .66f) })
            {
                Keys();
                Position(players[0], new Vector3(30.65f, .35f, 29.15f), Quaternion.identity);
                Position(players[1], new Vector3(29.35f, .35f, 29.15f), Quaternion.identity);
                yield return Wait(.22f);
                Keys("W", "UpArrow");
                yield return WaitForMoveInput(movers[0], Vector2.up);
                yield return WaitForMoveInput(movers[1], Vector2.up);
                yield return Wait(leadIn);
                var previous = new Vector3[players.Length][];
                var contacts = new int[players.Length];
                var peakShift = new float[players.Length];
                var maxSoleGap = new float[players.Length];
                var contactShift = new List<float>();
                for (int frame = 0; frame < 26; frame++)
                {
                    for (int i = 0; i < players.Length; i++)
                    {
                        shoes[i].BakeMesh(baked, true);
                        var surface = baked.vertices;
                        contactShift.Clear();
                        float lowest = float.PositiveInfinity;
                        for (int vertex = 0; vertex < surface.Length; vertex++)
                        {
                            Vector3 world = shoes[i].transform.TransformPoint(surface[vertex]);
                            lowest = Mathf.Min(lowest, world.y);
                            if (previous[i] != null && world.y <= nearFloor && previous[i][vertex].y <= nearFloor)
                                contactShift.Add(Vector3.ProjectOnPlane(world - previous[i][vertex], Vector3.up).magnitude);
                            surface[vertex] = world;
                        }
                        previous[i] = surface;
                        maxSoleGap[i] = Mathf.Max(maxSoleGap[i], lowest - tileHeight);
                        if (contactShift.Count < 6) continue;
                        contacts[i]++;
                        contactShift.Sort();
                        peakShift[i] = Mathf.Max(peakShift[i], contactShift[contactShift.Count / 2]);
                    }

                    Keys(angle == 0 ? "W" : angle == 90 ? "D" : "S",
                        angle == 0 ? "UpArrow" : angle == 90 ? "RightArrow" : "DownArrow");
                    yield return null;
                }

                for (int i = 0; i < players.Length; i++)
                {
                    string context = $"{players[i].name}, {angle} degrees after {leadIn:F2}s";
                    Assert.Less(Quaternion.Angle(players[i].transform.rotation, Quaternion.Euler(0f, angle, 0f)), 4f, context);
                    Assert.GreaterOrEqual(contacts[i], 18, context + " should establish repeated near-floor contact");
                    Assert.Less(peakShift[i], .045f, context + " shoe surface slides between near-floor frames");
                    if (angle == 180 && maxSoleGap[i] > .015f)
                        unsupportedPivots.Add($"{context}: both soles lifted by {maxSoleGap[i]:F4} m");
                    Debug.Log($"SOLE {context}: {contacts[i]}/25 contact samples, peak horizontal shift {peakShift[i]:F4} m, peak lowest sole gap {maxSoleGap[i]:F4} m");
                }
            }
            Assert.IsEmpty(unsupportedPivots, "A reversal must keep at least one shoe at the visible floor on every frame");
        }
        finally
        {
            Time.captureFramerate = captureRate;
            UnityEngine.Object.Destroy(baked);
            HoseCheck("ResetKeyboard");
        }
    }

    [UnityTest]
    public IEnumerator BothPlayersEaseBodyHeightAndSettleFeetWhenStopping()
    {
        EditorSceneManager.OpenScene(Scene);
        yield return new EnterPlayMode();
        yield return VerifyWalkTransitions();
    }

    static IEnumerator VerifyWalkTransitions()
    {
        yield return Ready();
        const float tileHeight = .20035f;
        var players = new[] { GameObject.Find("Player1"), GameObject.Find("Player2") };
        var visuals = players.Select(p => p.GetComponentInChildren(T("TA.AdaptiveAnimation.CharacterModelAnimatorMirror")).transform).ToArray();
        var shoes = visuals.Select(v => v.GetComponentsInChildren<SkinnedMeshRenderer>()
            .First(r => r.enabled && r.name == "Sepatu")).ToArray();
        var baked = new Mesh();
        int priorRate = Time.captureFramerate;
        Time.captureFramerate = 50;
        try
        {
            Position(players[0], new Vector3(30.65f, .35f, 29f), Quaternion.identity);
            Position(players[1], new Vector3(29.35f, .35f, 29f), Quaternion.identity);
            yield return Wait(.35f);
            var lastHeight = new float[players.Length];
            var maxHeightStep = new float[players.Length];
            var minSole = Enumerable.Repeat(float.PositiveInfinity, players.Length).ToArray();
            var maxSole = new float[players.Length];
            var settledHeight = new float[players.Length];
            for (int frame = 0; frame <= 72; frame++)
            {
                for (int i = 0; i < players.Length; i++)
                {
                    var shoe = shoes[i];
                    shoe.BakeMesh(baked, true);
                    float lowest = baked.vertices.Min(v => shoe.transform.TransformPoint(v).y);
                    float height = visuals[i].position.y - players[i].transform.position.y;
                    if (frame > 0) maxHeightStep[i] = Mathf.Max(maxHeightStep[i], Mathf.Abs(height - lastHeight[i]));
                    lastHeight[i] = height;
                    minSole[i] = Mathf.Min(minSole[i], lowest);
                    maxSole[i] = Mathf.Max(maxSole[i], lowest);
                    if (frame == 64) settledHeight[i] = height;
                }

                // Restart before the first stop's finishing step is complete.
                if ((frame >= 4 && frame < 30) || (frame >= 35 && frame < 41)) Keys("W", "UpArrow");
                else Keys();
                yield return null;
            }
            for (int i = 0; i < players.Length; i++)
            {
                string context = players[i].name;
                Assert.Greater(players[i].transform.position.z, 29.65f, context + " must walk on real input");
                Assert.Less(players[i].GetComponent<Rigidbody>().velocity.magnitude, .005f);
                Assert.Less(maxHeightStep[i], .016f, context + " visual body height should change gradually");
                Assert.That(minSole[i], Is.GreaterThan(tileHeight - .009f), context + " sole should stay above the floor");
                Assert.That(maxSole[i], Is.LessThan(tileHeight + .018f), context + " should keep a support shoe near the floor");
                Assert.That(lastHeight[i], Is.EqualTo(settledHeight[i]).Within(.003f), context + " idle height should settle");
                Debug.Log($"TRANSITION {context}: peak body step={maxHeightStep[i]:F4} m, sole y={minSole[i]:F4}..{maxSole[i]:F4} m");
            }
        }
        finally
        {
            Time.captureFramerate = priorRate;
            UnityEngine.Object.Destroy(baked);
            HoseCheck("ResetKeyboard");
        }
    }

    [Test]
    public void ActiveWalkingClipHasContinuousPoseAndVelocityAtItsSeam()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Arts/Character/Mulyono.controller");
        var state = controller.layers[0].stateMachine.states.First(s => s.state.name == "Walking").state;
        var clip = state.motion as AnimationClip;
        Assert.IsNotNull(clip);
        Assert.AreEqual("Running", clip.name, "Verify the clip actually assigned to walking");
        Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(clip).loopTime);
        const float epsilon = .0005f;
        float maximumPositionDerivative = 0f, maximumRotationDerivative = 0f;
        int checkedCurves = 0;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            bool position = binding.propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal);
            bool rotation = binding.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal);
            if (binding.type != typeof(Transform) || (!position && !rotation)) continue;
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            float start = curve.Evaluate(0f), end = curve.Evaluate(clip.length);
            Assert.Less(Mathf.Abs(start - end), .0002f, binding.path + " " + binding.propertyName + " pose seam");
            float outgoing = (curve.Evaluate(epsilon) - start) / epsilon;
            float incoming = (end - curve.Evaluate(clip.length - epsilon)) / epsilon;
            float difference = Mathf.Abs(outgoing - incoming);
            if (position) maximumPositionDerivative = Mathf.Max(maximumPositionDerivative, difference);
            else maximumRotationDerivative = Mathf.Max(maximumRotationDerivative, difference);
            Assert.Less(difference, position ? .01f : .08f,
                binding.path + " " + binding.propertyName + " loop velocity must not reverse abruptly");
            checkedCurves++;
        }
        Assert.Greater(checkedCurves, 30, "The active skeleton's motion curves must actually be checked");
        Debug.Log("WALK seam curves=" + checkedCurves + " position derivative delta=" + maximumPositionDerivative +
            " quaternion derivative delta=" + maximumRotationDerivative);
    }

    [UnityTest]
    public IEnumerator BothPlayersWalkFasterWithMatchedCadenceAndStopCleanly()
    {
        EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return Ready();
        var p1 = GameObject.Find("Player1"); var p2 = GameObject.Find("Player2");
        Assert.IsNotNull(p1); Assert.IsNotNull(p2);
        foreach (int slot in new[] { 0, 1 })
        {
            var player = slot == 0 ? p1 : p2; var other = slot == 0 ? p2 : p1;
            Position(player, new Vector3(28f, .35f, 28f), Quaternion.identity);
            Position(other, new Vector3(35f, .35f, 28f), Quaternion.identity);
            var mover = C(player, "ThirdPersonPlayerController"); var animator = player.GetComponent<Animator>();
            Assert.That((float)P(mover, "BaseMoveSpeed"), Is.EqualTo(1f).Within(.0001f));
            Assert.That((float)P(mover, "UnladenMoveSpeed"), Is.EqualTo(1.5f).Within(.0001f));
            yield return Wait(.3f);
            Vector3 otherStart = other.transform.position;
            Keys(slot == 0 ? "D" : "RightArrow"); yield return Wait(.5f);
            Assert.IsTrue(animator.GetBool("isWalking"));
            Vector3 start = player.GetComponent<Rigidbody>().position; float started = Time.time;
            yield return Wait(.8f);
            float speed = Vector3.ProjectOnPlane(player.GetComponent<Rigidbody>().position - start, Vector3.up).magnitude / (Time.time - started);
            float rate = animator.GetFloat("LocomotionRate");
            Assert.That(speed, Is.EqualTo(1.5f).Within(.08f), player.name + " steady free-walk speed");
            Assert.That(rate, Is.EqualTo(speed).Within(.10f), player.name + " gait rate follows actual free-walk motion");
            Assert.Less(Vector3.Distance(otherStart, other.transform.position), .03f, "Unpaired player remains still");
            Keys(); yield return Wait(.25f);
            Assert.Less(((Vector3)P(mover, "LocomotionVelocity")).magnitude, .005f);
            Assert.IsFalse(animator.GetBool("isWalking"), "Stop must not retain the walking state");
            yield return Wait(.15f);
            Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle 0"));
            Keys(slot == 0 ? "D" : "RightArrow"); yield return Wait(.2f);
            Assert.IsTrue(animator.GetBool("isWalking"), "Walking must restart after stopping");
            Keys(); yield return Wait(.3f);
            Assert.IsFalse(animator.GetBool("isWalking"));
            Debug.Log("WALK free " + player.name + " actual=" + speed + " rate=" + rate);
        }
    }

    [UnityTest]
    public IEnumerator BothPlayersTurnProgressivelyAndStopPromptly()
    {
        EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return Ready();
        var p1 = GameObject.Find("Player1"); var p2 = GameObject.Find("Player2");
        foreach (int slot in new[] { 0, 1 })
        {
            var player = slot == 0 ? p1 : p2; var other = slot == 0 ? p2 : p1;
            var mover = C(player, "ThirdPersonPlayerController");
            var animator = player.GetComponent<Animator>();
            var body = player.GetComponent<Rigidbody>();
            Position(player, new Vector3(28f, .35f, 28f), Quaternion.LookRotation(Vector3.right));
            Position(other, new Vector3(35f, .35f, 28f), Quaternion.identity);
            yield return Wait(.25f);
            Keys(slot == 0 ? "D" : "RightArrow");
            yield return WaitForMoveInput(mover, Vector2.right);
            yield return Wait(.15f);
            Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Walking") && !animator.IsInTransition(0),
                player.name + " should reach the visible gait within a short input tap");
            Assert.Greater(animator.GetFloat("LocomotionRate"), 1.2f, "Cadence should catch up to the launch");
            yield return Wait(.25f);
            Keys(slot == 0 ? "A" : "LeftArrow");
            yield return WaitForMoveInput(mover, Vector2.left);
            float turnStart = Time.fixedTime;
            while (Time.fixedTime - turnStart < .065f) yield return null;
            float earlyError = Vector3.Angle(body.rotation * Vector3.forward, Vector3.left);
            var earlyVelocity = (Vector3)P(mover, "LocomotionVelocity");
            float bank = ((Vector2)P(mover, "WalkBodyLean")).y;
            Assert.That(earlyError, Is.InRange(50f, 165f), "A U-turn must visibly pass through intermediate headings");
            Assert.Less(earlyVelocity.magnitude, .8f, "A sharp reversal brakes before accelerating in the new direction");
            Assert.Greater(Mathf.Abs(bank), 1f, "The torso should bank even during an exact reversal");
            Assert.IsTrue(animator.GetBool("isWalking"), "A planted pivot retains the current gait phase");
            while (Time.fixedTime - turnStart < .36f) yield return null;
            var velocity = (Vector3)P(mover, "LocomotionVelocity");
            float turnError = Vector3.Angle(body.rotation * Vector3.forward, Vector3.left);
            Assert.Less(velocity.x, -1.35f, "A reversal should quickly reach backward world travel");
            Assert.Less(turnError, 12f, "Character should turn with the new travel direction");
            // Start a right-angle corner and release before its turn settles.
            Keys(slot == 0 ? "W" : "UpArrow");
            yield return WaitForMoveInput(mover, Vector2.up);
            for (float until = Time.fixedTime + .11f; Time.fixedTime < until;) yield return null;
            var cornerVelocity = (Vector3)P(mover, "LocomotionVelocity");
            Assert.Less(cornerVelocity.x, -.15f, "A corner curves away from the old direction progressively");
            Assert.Greater(cornerVelocity.z, .2f, "The curve still responds to the new input");
            Keys(); yield return WaitForMoveInput(mover, Vector2.zero);
            Vector3 beforeRelease = body.position;
            Quaternion headingOnRelease = body.rotation;
            yield return Wait(.15f);
            float coast = (body.position - beforeRelease).magnitude;
            Assert.Less(coast, .08f, "The character should brake close to the release point");
            Assert.That(((Vector3)P(mover, "LocomotionVelocity")).magnitude, Is.LessThan(.005f));
            Assert.Less(Quaternion.Angle(headingOnRelease, body.rotation), 1f, "A released turn must not continue spinning");
            Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle 0") && !animator.IsInTransition(0),
                "The walk blend should finish promptly once the player releases movement");
            Debug.Log($"WALK turn {player.name}: early error={earlyError:F1}, bank={bank:F1}, recovered speed={velocity.x:F3}, final error={turnError:F2}, corner={cornerVelocity}, coast={coast:F3}");
        }
    }

    static IEnumerator WaitForMoveInput(Component mover, Vector2 expected)
    {
        // Start timing after Update consumes the new key state, including when
        // an editor frame runs several physics ticks before processing input.
        var input = mover.GetType().GetField("_inputVector", Flags);
        for (float deadline = Time.time + .5f; (Vector2)input.GetValue(mover) != expected && Time.time < deadline;)
            yield return null;
        Assert.AreEqual(expected, (Vector2)input.GetValue(mover));
    }

    [UnityTest]
    public IEnumerator FreeWalkIncreaseDoesNotChangeLoadedComputerSpeed()
    {
        EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return Ready();
        var player = GameObject.Find("Player1"); var actor = C(player, "HPlayer.InteractionController");
        var mover = C(player, "ThirdPersonPlayerController");
        Position(player, new Vector3(30f, .35f, 28f), Quaternion.identity);
        Position(GameObject.Find("Player2"), new Vector3(35f, .35f, 28f), Quaternion.identity);
        var computer = GameObject.Find("Carry_Props_Computer_01");
        Position(computer, new Vector3(30f, .75f, 28.65f), Quaternion.identity);
        var item = C(computer, "HInteractions.Liftable");
        Call(C(computer, "TA.AdaptiveAnimation.CarryAnimationLoad"), "Configure", 30f);
        yield return Wait(.2f); Call(actor, "PickUpObject", item); yield return Wait(.8f);
        Assert.AreSame(item, P(actor, "HeldObject"));
        float expected = (float)P(mover, "BaseMoveSpeed") * (1f - (float)P(item, "SpeedPenalty"));
        Assert.That((float)P(mover, "BaseMoveSpeed"), Is.EqualTo(1f).Within(.0001f));
        Assert.Greater((float)P(mover, "UnladenMoveSpeed"), (float)P(mover, "BaseMoveSpeed"));
        Keys("D"); yield return Wait(.5f);
        Vector3 start = player.GetComponent<Rigidbody>().position; float started = Time.time;
        yield return Wait(.8f);
        float actual = Vector3.ProjectOnPlane(player.GetComponent<Rigidbody>().position - start, Vector3.up).magnitude / (Time.time - started);
        Assert.That(actual, Is.EqualTo(expected).Within(.055f), "Loaded movement keeps the established base-speed penalty");
        Assert.That(player.GetComponent<Animator>().GetFloat("LocomotionRate"), Is.EqualTo(actual).Within(.07f));
        Keys(); Call(actor, "ReleaseHeldObject"); yield return Wait(.8f);
        // The released computer remains solid. Move the fixture clear before
        // measuring unladen recovery rather than walking directly into it.
        Position(computer, new Vector3(35f, .75f, 35f), Quaternion.identity);
        Position(player, new Vector3(28f, .35f, 28f), Quaternion.identity);
        Keys("D"); yield return Wait(.5f);
        Assert.That(((Vector3)P(mover, "LocomotionVelocity")).magnitude, Is.EqualTo(1.5f).Within(.08f));
        Keys();
        Debug.Log("WALK loaded expected=" + expected + " measured=" + actual + " free=" + P(mover, "UnladenMoveSpeed"));
    }

    sealed class HeadShape
    {
        public SkinnedMeshRenderer renderer;
        public Vector3[] samples;
        public Vector3 centroid;
    }
    static SkinnedMeshRenderer[] HeadSkins(GameObject player, Transform proxy)
    {
        var skins = new List<SkinnedMeshRenderer>();
        foreach (var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (renderer.enabled && renderer.bones.Length == 1 && renderer.bones[0] == proxy) skins.Add(renderer);
        return skins.ToArray();
    }
    static HeadShape Shape(SkinnedMeshRenderer renderer, Transform proxy, Mesh scratch)
    {
        renderer.BakeMesh(scratch, true);
        var vertices = scratch.vertices;
        Assert.Greater(vertices.Length, 0);
        var samples = new Vector3[Mathf.Min(32, vertices.Length)];
        Quaternion inverseRotation = Quaternion.Inverse(proxy.rotation);
        Vector3 centroid = Vector3.zero;
        var worldBounds = renderer.bounds; worldBounds.Expand(.02f);
        bool enclosed = true;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = renderer.transform.TransformPoint(vertices[i]);
            centroid += inverseRotation * (world - proxy.position);
            if (!worldBounds.Contains(world)) enclosed = false;
        }
        Assert.IsTrue(enclosed, renderer.name + " culling bounds must enclose its actual baked geometry");
        for (int i = 0; i < samples.Length; i++)
        {
            int index = samples.Length == 1 ? 0 : i * (vertices.Length - 1) / (samples.Length - 1);
            Vector3 world = renderer.transform.TransformPoint(vertices[index]);
            // Remove only rigid world pose. InverseTransformPoint would cancel
            // nonuniform scale/shear and conceal the original head distortion.
            samples[i] = inverseRotation * (world - proxy.position);
        }
        return new HeadShape { renderer = renderer, samples = samples, centroid = centroid / vertices.Length };
    }
    static HeadShape[] CaptureShapes(SkinnedMeshRenderer[] skins, Transform proxy, Mesh scratch)
    {
        var shapes = new HeadShape[skins.Length];
        for (int i = 0; i < skins.Length; i++) shapes[i] = Shape(skins[i], proxy, scratch);
        return shapes;
    }
    static float CheckShapes(HeadShape[] reference, Transform proxy, Mesh scratch)
    {
        float maximum = 0f;
        foreach (var rest in reference)
        {
            var current = Shape(rest.renderer, proxy, scratch);
            Assert.AreEqual(rest.samples.Length, current.samples.Length);
            Assert.Less(Vector3.Distance(rest.centroid, current.centroid), .002f,
                rest.renderer.name + " facial placement must stay fixed relative to the head");
            for (int i = 0; i < rest.samples.Length; i++) maximum = Mathf.Max(maximum, Vector3.Distance(rest.samples[i], current.samples[i]));
        }
        Assert.Less(maximum, .002f, "Skull and facial geometry must remain rigid through the actual gait");
        return maximum;
    }

    [UnityTest]
    public IEnumerator HeadAndFaceRemainRigidThroughFullWalkCyclesAndTurning()
    {
        EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return Ready();
        var p1 = GameObject.Find("Player1"); var p2 = GameObject.Find("Player2");
        foreach (int slot in new[] { 0, 1 })
        {
            var player = slot == 0 ? p1 : p2; var other = slot == 0 ? p2 : p1;
            Position(player, new Vector3(28f, .35f, 28f), Quaternion.identity);
            Position(other, new Vector3(35f, .35f, 28f), Quaternion.identity);
            yield return Wait(.4f);
            var follower = player.GetComponentInChildren(T("TA.AdaptiveAnimation.CharacterRigidHeadFollower"));
            Assert.IsNotNull(follower, "The spawned model must use the saved rigid-head follow setup");
            var proxy = (Transform)P(follower, "RigidRoot"); var source = (Transform)P(follower, "SourceHead");
            var skins = HeadSkins(player, proxy);
            CollectionAssert.AreEquivalent(new[] { "Kepala", "Alis", "Bulu_Mata", "Gigi", "Hidung", "LIdah", "Mata", "Telinga", "Topi_Pemadam" },
                skins.Where(s => !s.name.EndsWith(" (Outline)", StringComparison.Ordinal)).Select(s => s.name).ToArray(),
                "The full face and helmet must follow the same rigid head");
            foreach (var skin in skins)
            {
                Assert.AreSame(proxy, skin.rootBone);
                Assert.IsTrue(AssetDatabase.Contains(skin.sharedMesh), "Runtime head shape must use persistent mesh assets");
            }
            var scratch = new Mesh();
            float maximum = 0f;
            try
            {
                var reference = CaptureShapes(skins, proxy, scratch);
                Keys(slot == 0 ? "W" : "UpArrow");
                for (int sample = 0; sample < 40; sample++)
                {
                    yield return Wait(.05f);
                    if (sample == 20) Keys(slot == 0 ? "D" : "RightArrow");
                    Assert.Less(Vector3.Distance(proxy.position, source.position), .001f, "Rigid head must follow the mirrored head position");
                    Assert.Less(Quaternion.Angle(proxy.rotation, source.rotation), .1f, "Head look and lean rotation must remain available");
                    Vector3 scale = proxy.lossyScale;
                    Assert.Less(Mathf.Abs(scale.x - scale.y), .0001f);
                    Assert.Less(Mathf.Abs(scale.x - scale.z), .0001f);
                    maximum = Mathf.Max(maximum, CheckShapes(reference, proxy, scratch));
                }
                Keys(); yield return Wait(.3f);
                maximum = Mathf.Max(maximum, CheckShapes(reference, proxy, scratch));
                Debug.Log("WALK head " + player.name + " skins=" + skins.Length + " maximum rigid shape drift=" + maximum);
            }
            finally { UnityEngine.Object.DestroyImmediate(scratch); }
        }
    }
}
