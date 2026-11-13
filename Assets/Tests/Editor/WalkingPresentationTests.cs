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
    static IEnumerator Ready()
    {
        HoseCheck("ResetKeyboard");
        var adapter = C(GameObject.Find("FR_Level_1"), "TA.AdaptiveAnimation.AdaptiveSceneProps");
        for (float until = Time.time + 3f; !(bool)P(adapter, "IsReady") && Time.time < until;) yield return null;
        Assert.IsTrue((bool)P(adapter, "IsReady"));
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
