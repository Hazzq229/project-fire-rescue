using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

// Reflection keeps this test assembly independent of the project's Assembly-CSharp.
public class AdaptiveCarryTests
{
    [Test]
    public void FootPlantCadenceMatchesDemoMovement()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        var controller = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
            "Assets/Arts/Character/Mulyono.controller");
        foreach (var playerName in new[] { "Player1", "Player2" })
        {
            var player = GameObject.Find(playerName);
            var movement = player.GetComponent(T("ThirdPersonPlayerController"));
            float baseSpeed = (float)Prop(movement, "BaseMoveSpeed");
            float referenceSpeed = (float)Get(movement, "_walkAnimationReferenceSpeed");
            var clone = UnityEngine.Object.Instantiate(player);
            clone.SetActive(false);
            try
            {
                var slot = clone.GetComponent(T("TA.AdaptiveAnimation.CharacterModelSlot"));
                var root = (Transform)Get(slot, "skeletonRoot");
                var foot = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "foot.L");
                var visualRoot = (GameObject)Get(slot, "generatedVisuals");
                var visualFoot = visualRoot.GetComponentsInChildren<Transform>(true).First(t => t.name == "foot.L");
                var mirror = clone.GetComponentInChildren(T("TA.AdaptiveAnimation.CharacterModelAnimatorMirror"), true);
                var state = controller.layers.SelectMany(l => l.stateMachine.states)
                    .First(s => s.state.name == "Walking").state;
                var clip = (AnimationClip)state.motion;
                Assert.AreEqual("Running", clip.name, "Walking uses the selected running gait");
                clip.SampleAnimation(clone, 1.18f);
                Call(mirror, "LateUpdate");
                float first = clone.transform.InverseTransformPoint(foot.position).z;
                float visualFirst = clone.transform.InverseTransformPoint(visualFoot.position).z;
                clip.SampleAnimation(clone, 1.77f);
                Call(mirror, "LateUpdate");
                float second = clone.transform.InverseTransformPoint(foot.position).z;
                float visualSecond = clone.transform.InverseTransformPoint(visualFoot.position).z;
                float rate = baseSpeed / referenceSpeed;
                float gaitCycleSeconds = clip.length * .5f / (state.speed * rate);
                Assert.That(gaitCycleSeconds, Is.GreaterThan(.45f), playerName + " gait should read as a walk");
                float rootTravel = baseSpeed * .59f / (state.speed * rate);
                Assert.That(Mathf.Abs(rootTravel + second - first), Is.LessThan(.04f),
                    playerName + " planted foot should stay near its world position");
                Assert.That(Mathf.Abs(rootTravel + visualSecond - visualFirst), Is.LessThan(.05f),
                    playerName + " visible planted foot should stay near its world position");
            }
            finally { UnityEngine.Object.DestroyImmediate(clone); }
        }
    }
    [Test]
    public void WalkingLoopReturnsToItsStartingPose()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        var clone = UnityEngine.Object.Instantiate(GameObject.Find("Player1"));
        clone.SetActive(false);
        try
        {
            var root = (Transform)Get(clone.GetComponent(T("TA.AdaptiveAnimation.CharacterModelSlot")), "skeletonRoot");
            var bones = root.GetComponentsInChildren<Transform>(true);
            var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Arts/Character/Walking.anim");
            Assert.IsTrue(UnityEditor.AnimationUtility.GetAnimationClipSettings(clip).loopTime);
            clip.SampleAnimation(clone, 0f);
            var startingRotations = bones.Select(b => b.localRotation).ToArray();
            var startingPositions = bones.Select(b => b.localPosition).ToArray();
            clip.SampleAnimation(clone, clip.length);
            for (int i = 0; i < bones.Length; i++)
            {
                Assert.That(Quaternion.Angle(startingRotations[i], bones[i].localRotation), Is.LessThan(1f), bones[i].name + " rotation at loop seam");
                Assert.That(Vector3.Distance(startingPositions[i], bones[i].localPosition), Is.LessThan(.005f), bones[i].name + " position at loop seam");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
    }
    [Test]
    public void DashPoseTrailsHandsAndFeetBehindForwardLean()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        var clone = UnityEngine.Object.Instantiate(GameObject.Find("Player1"));
        clone.SetActive(false);
        try
        {
            var root = (Transform)Get(clone.GetComponent(T("TA.AdaptiveAnimation.CharacterModelSlot")), "skeletonRoot");
            var bones = root.GetComponentsInChildren<Transform>(true);
            var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Arts/Character/Dash.anim");
            Assert.IsFalse(UnityEditor.AnimationUtility.GetAnimationClipSettings(clip).loopTime);
            clip.SampleAnimation(clone, .2f);
            Transform lowerSpine = bones.First(b => b.name == "spine.001");
            Transform upperSpine = bones.First(b => b.name == "spine.003");
            Assert.That(clone.transform.InverseTransformVector(upperSpine.position - lowerSpine.position).z,
                Is.GreaterThan(.06f), "Torso leans forward");
            foreach (var side in new[] { "L", "R" })
            {
                Transform arm = bones.First(b => b.name == "upper_arm." + side);
                Transform hand = bones.First(b => b.name == "hand." + side);
                Transform thigh = bones.First(b => b.name == "thigh." + side);
                Transform foot = bones.First(b => b.name == "foot." + side);
                Assert.That(clone.transform.InverseTransformVector(hand.position - arm.position).z,
                    Is.LessThan(-.12f), side + " hand trails behind");
                Assert.That(clone.transform.InverseTransformVector(foot.position - thigh.position).z,
                    Is.LessThan(-.06f), side + " foot trails behind");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
    }
    [UnityTest]
    public IEnumerator DemoPlayersHaveIndependentKeyboardSchemes()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        yield return null;
        foreach (var pair in new[] { new { name = "Player1", scheme = "KeyboardWASD" },
            new { name = "Player2", scheme = "KeyboardArrows" } })
        {
            var player = GameObject.Find(pair.name);
            var input = player.GetComponent(T("UnityEngine.InputSystem.PlayerInput"));
            Assert.AreEqual(pair.scheme, Prop(input, "currentControlScheme"), pair.name + " keyboard scheme");
            Assert.IsNotNull(Prop(input, "currentActionMap"), pair.name + " action map enabled");
            Assert.That((int)Prop(Prop(Prop(input, "user"), "pairedDevices"), "Count"), Is.GreaterThan(0), pair.name + " keyboard paired");
        }
        yield return new ExitPlayMode();
    }
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(name)).First(t => t != null);
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Get(object o, string field)
    {
        for (var type = o.GetType(); type != null; type = type.BaseType)
        {
            var info = type.GetField(field, Flags);
            if (info != null) return info.GetValue(o);
        }
        throw new MissingFieldException(o.GetType().Name, field);
    }
    static void Set(object o, string field, object value) => o.GetType().GetField(field, Flags).SetValue(o, value);
    static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, Flags).Invoke(o, args);
    static object Prop(object o, string name) => o.GetType().GetProperty(name).GetValue(o);

    [Test]
    public void CooperativeInputRequiresAgreement()
    {
        var method = T("TA.AdaptiveAnimation.CooperativeCarryObject").GetMethod("CombineInput");
        Assert.AreEqual(Vector2.zero, method.Invoke(null, new object[] { Vector2.up, Vector2.zero }));
        Assert.AreEqual(Vector2.zero, method.Invoke(null, new object[] { Vector2.up, Vector2.down }));
        Assert.AreEqual(Vector2.up, method.Invoke(null, new object[] { Vector2.up, Vector2.up }));
    }

    [UnityTest]
    public IEnumerator CooperativeRolesPickupGripAndRelease()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        for (float until = Time.realtimeSinceStartup + .3f; Time.realtimeSinceStartup < until;) yield return null;
        var p1 = GameObject.Find("Player1");
        var p2 = GameObject.Find("Player2");
        var i1 = p1.GetComponent(T("HPlayer.InteractionController"));
        var i2 = p2.GetComponent(T("HPlayer.InteractionController"));
        var box = GameObject.Find("Coop_HeavyCrate");
        var coop = box.GetComponent(T("TA.AdaptiveAnimation.CooperativeCarryObject"));
        var playerInput = Prop(p2.GetComponent(T("HPlayer.LocalPlayerInput")), "PlayerInputComponent");
        Assert.IsNotNull(playerInput, "Player2 must have a PlayerInput component");
        Assert.IsNotNull(Prop(playerInput, "actions"), "Player2 must have input actions assigned");
        // Go through the actual candidate, Animator state and Pickup_Grab event path.
        Call(i1, "BeginInteraction");
        for (float until = Time.realtimeSinceStartup + 2f; Time.realtimeSinceStartup < until;) yield return null;
        Assert.AreSame(coop, Prop(i1, "HeldObject"), "Player1 must acquire the crate via pickup animation");
        Assert.AreEqual("Back", Prop(i1, "CurrentCarryRole").ToString());
        Assert.AreEqual("WaitingForPartner", Prop(coop, "State").ToString());
        Assert.That(((Vector3)Prop(coop, "GroupVelocity")).magnitude, Is.LessThan(.001f));
        Assert.That((float)Prop(coop, "LoadPerPlayerKg"), Is.EqualTo(60f));
        var heavyRb = box.GetComponent<Rigidbody>();
        Assert.That(Quaternion.Angle(heavyRb.rotation, Quaternion.identity), Is.GreaterThan(5f));
        Assert.That(box.GetComponent<BoxCollider>().bounds.min.y, Is.GreaterThan(.47f), "Unheld end must not penetrate the floor");
        for (float until = Time.time + 1f; Time.time < until;) yield return null;
        var soloGrip = p1.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectGripController"));
        Debug.Log("Solo grip: " + JsonUtility.ToJson(soloGrip));
        Capture("Temp/AdaptiveCarrySoloTilt.png");
        Assert.That((float)Get(soloGrip, "currentWeight"), Is.GreaterThan(.99f));
        AssertHandContact(p1);
        var soloSimulationMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        try
        {
            Vector3 start = heavyRb.position;
            Quaternion tilt = heavyRb.rotation;
            for (int step = 0; step < 25; step++)
            {
                Call(coop, "MoveSolo", Vector2.right);
                Physics.Simulate(Time.fixedDeltaTime);
            }
            float expectedSoloSpeed = (float)Prop(p1.GetComponent(T("ThirdPersonPlayerController")), "BaseMoveSpeed") *
                (1f - (float)Prop(coop, "SpeedPenalty"));
            Assert.That(heavyRb.position.x - start.x,
                Is.EqualTo(expectedSoloSpeed * 25 * Time.fixedDeltaTime).Within(.01f));
            Assert.That(Quaternion.Angle(tilt, heavyRb.rotation), Is.LessThan(.1f));
            Assert.That(heavyRb.position.y, Is.EqualTo(start.y).Within(.01f));
        }
        finally { Physics.simulationMode = soloSimulationMode; }
        Call(i2, "BeginInteraction");
        for (float until = Time.time + 3f; !((UnityEngine.Object)Prop(i2, "HeldObject")) && Time.time < until;) yield return null;
        Assert.IsTrue((bool)Prop(i2, "IsPickingUp"), "Second grab must attach before pickup exits");
        Assert.That(Quaternion.Angle(heavyRb.rotation, Quaternion.identity), Is.LessThan(.2f), "Box must level on second grab, without waiting for clip exit");
        for (float until = Time.time + 4f; Time.time < until;) yield return null;
        Assert.AreSame(coop, Prop(i2, "HeldObject"), "Player2 must detect the already held crate");
        Assert.AreEqual("Front", Prop(i2, "CurrentCarryRole").ToString());
        Assert.AreEqual("Carrying", Prop(coop, "State").ToString());
        Assert.That((float)Prop(coop, "LoadPerPlayerKg"), Is.EqualTo(30f));
        Assert.That((float)Prop(coop, "SpeedPenalty"), Is.EqualTo(.35f).Within(.0001f));
        Assert.That(Quaternion.Angle(heavyRb.rotation, Quaternion.identity), Is.LessThan(.2f));
        Capture("Temp/AdaptiveCarryCooperative.png");
        foreach (var p in new[] { p1, p2 })
        {
            var grip = p.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectGripController"));
            Debug.Log(p.name + " grip: " + JsonUtility.ToJson(grip));
            Assert.That((float)Get(grip, "currentWeight"), Is.GreaterThan(.99f), p.name + " grip must remain reachable");
            Assert.IsEmpty(p.GetComponents<SpringJoint>(), "Cooperative movement must not fight spring joints");
            AssertHandContact(p);
        }
        Capture("Temp/AdaptiveCarryCooperative.png");
        // World-direction effort must remain correct for opposite-facing character rigs.
        PumpEffort(p1, p2);
        var neutral1 = Chest(p1).rotation;
        var neutral2 = Chest(p2).rotation;
        Call(coop, "UpdateEffort", Vector2.right, Vector2.left);
        PumpEffort(p1, p2);
        AssertEffort(p1, Vector3.left);
        AssertEffort(p2, Vector3.right);
        AssertBoneEffort(p1, neutral1, Vector3.left);
        AssertBoneEffort(p2, neutral2, Vector3.right);
        Capture("Temp/AdaptiveCarryConflictingInput.png");
        Call(coop, "UpdateEffort", Vector2.up, Vector2.down);
        PumpEffort(p1, p2);
        AssertEffort(p1, Vector3.back);
        AssertEffort(p2, Vector3.forward);
        Call(coop, "UpdateEffort", Vector2.right, Vector2.right);
        PumpEffort(p1, p2);
        Assert.That(((Vector3)Get(p1.GetComponent(T("TA.AdaptiveAnimation.HeldObjectBodyLeanController")), "currentEffortRotation")).magnitude, Is.LessThan(.01f));
        // Drive the same movement integration used by FixedUpdate, then measure actual physics.
        var simulationMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        try
        {
            var rb = box.GetComponent<Rigidbody>();
            Vector3 start = rb.position;
            Vector3 separation = p1.GetComponent<Rigidbody>().position - p2.GetComponent<Rigidbody>().position;
            for (int step = 0; step < 25; step++)
            {
                Call(coop, "MoveTogether", Vector2.right, Vector2.right);
                Physics.Simulate(Time.fixedDeltaTime);
            }
            float expectedTogetherSpeed = ((float)Prop(p1.GetComponent(T("ThirdPersonPlayerController")), "BaseMoveSpeed") +
                (float)Prop(p2.GetComponent(T("ThirdPersonPlayerController")), "BaseMoveSpeed")) * .5f *
                (1f - (float)Prop(coop, "SpeedPenalty"));
            Assert.That(rb.position.x - start.x, Is.EqualTo(expectedTogetherSpeed * 25 * Time.fixedDeltaTime).Within(.01f));
            Assert.That(Mathf.Abs(separation.magnitude -
                (p1.GetComponent<Rigidbody>().position - p2.GetComponent<Rigidbody>().position).magnitude),
                Is.LessThan(.01f), "Steering may rotate the pair, but must preserve their spacing");
            start = rb.position;
            Call(coop, "MoveTogether", Vector2.right, Vector2.left);
            Physics.Simulate(Time.fixedDeltaTime);
            Assert.That(Vector3.Distance(start, rb.position), Is.LessThan(.002f));
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.name = "Temporary test wall";
            obstacle.transform.position = rb.position + Vector3.right * .8f;
            obstacle.transform.localScale = new Vector3(.2f, 3f, 5f);
            Physics.SyncTransforms();
            for (int step = 0; step < 30; step++)
            {
                Call(coop, "MoveTogether", Vector2.right, Vector2.right);
                Physics.Simulate(Time.fixedDeltaTime);
            }
            Assert.That(rb.position.x, Is.LessThan(obstacle.transform.position.x - .3f), "Crate must stop before the wall");
            UnityEngine.Object.Destroy(obstacle);
        }
        finally { Physics.simulationMode = simulationMode; }
        Call(i1, "BeginPlaceDown");
        Assert.IsTrue((bool)Prop(i1, "IsPlacingDown"));
        Assert.IsTrue((bool)Prop(i2, "IsPlacingDown"));
        for (float until = Time.time + 3.5f; Time.time < until;) yield return null;
        Assert.IsNull(Prop(i1, "HeldObject"));
        Assert.IsNull(Prop(i2, "HeldObject"));
        Assert.AreEqual("None", Prop(i2, "CurrentCarryRole").ToString());
        Assert.IsFalse(box.GetComponent<Rigidbody>().isKinematic);
        Assert.AreEqual("Idle", Prop(coop, "State").ToString());
        // Let the released crate settle onto the floor before the next solo lift.
        for (float until = Time.time + 1f; Time.time < until;) yield return null;
        // Swap sides and pickup order: player number must never determine role.
        p1.GetComponent<Rigidbody>().position = (Vector3)Call(coop, "GetStance", Enum.Parse(T("TA.AdaptiveAnimation.CarryRole"), "Front"), .68f);
        p2.GetComponent<Rigidbody>().position = (Vector3)Call(coop, "GetStance", Enum.Parse(T("TA.AdaptiveAnimation.CarryRole"), "Back"), .68f);
        Physics.SyncTransforms();
        Call(i1, "PickUpObject", coop);
        for (float until = Time.time + 2f; Time.time < until;) yield return null;
        Assert.That(Vector3.Dot(box.GetComponent<Rigidbody>().rotation * Vector3.forward, Vector3.up), Is.GreaterThan(.08f), "Front-first pickup raises the front end");
        AssertHandContact(p1);
        Call(i2, "PickUpObject", coop);
        Assert.AreEqual("Front", Prop(i1, "CurrentCarryRole").ToString());
        Assert.AreEqual("Back", Prop(i2, "CurrentCarryRole").ToString());
        p2.SetActive(false);
        Assert.IsNull(Prop(i1, "HeldObject"), "Disabling partner must release both holders");
        yield return new ExitPlayMode();
    }

    static Transform Chest(GameObject player)
    {
        var lean = player.GetComponent(T("TA.AdaptiveAnimation.HeldObjectBodyLeanController"));
        var data = Get(Get(lean, "bodyLean"), "m_Data");
        return (Transform)data.GetType().GetField("chest").GetValue(data);
    }

    static void AssertBoneEffort(GameObject player, Quaternion neutral, Vector3 direction)
    {
        var delta = Chest(player).rotation * Quaternion.Inverse(neutral);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        float directedAngle = Vector3.Dot(axis * angle, Vector3.Cross(Vector3.up, direction));
        Assert.That(directedAngle, Is.InRange(5f, 11f), player.name + " chest must visibly lean toward its world input");
    }

    static void PumpEffort(params GameObject[] players)
    {
        for (int frame = 0; frame < 60; frame++)
            foreach (var p in players)
            {
                Call(p.GetComponent(T("TA.AdaptiveAnimation.HeldObjectBodyLeanController")), "Update");
                Call(p.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectGripController")), "Update");
                Call(p.GetComponent(T("UnityEngine.Animations.Rigging.RigBuilder")), "Evaluate", 1f / 60f);
            }
    }

    static void AssertEffort(GameObject player, Vector3 expectedDirection)
    {
        var lean = player.GetComponent(T("TA.AdaptiveAnimation.HeldObjectBodyLeanController"));
        var rotation = (Vector3)Get(lean, "currentEffortRotation");
        Vector3 expectedAxis = Vector3.Cross(Vector3.up, expectedDirection);
        Assert.That(Vector3.Dot(rotation.normalized, expectedAxis), Is.GreaterThan(.99f));
        Assert.That(rotation.magnitude, Is.GreaterThan(7.9f));
        var constraint = Get(lean, "bodyLean");
        var data = Get(constraint, "m_Data");
        Assert.That(Vector3.Distance((Vector3)data.GetType().GetField("effortRotationWorld").GetValue(data), rotation), Is.LessThan(.001f));
        AssertHandContact(player);
    }

    static void AssertHandContact(GameObject player)
    {
        var grip = player.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectGripController"));
        foreach (string side in new[] { "leftArmIK", "rightArmIK" })
        {
            var data = Get(Get(grip, side), "m_Data");
            var tip = (Transform)Prop(data, "tip");
            var target = (Transform)Prop(data, "target");
            Assert.That(Vector3.Distance(tip.position, target.position), Is.LessThan(.035f), player.name + " " + side + " contact");
        }
    }

    static void Capture(string path, GameObject focus = null)
    {
        var camera = Camera.main;
        var cameraPosition = camera.transform.position;
        var cameraRotation = camera.transform.rotation;
        var subject = focus ? focus : GameObject.Find("Coop_HeavyCrate");
        camera.transform.position = subject.transform.position + (focus
            ? subject.transform.forward * 3f + subject.transform.right * 2.4f + Vector3.up * 1.6f
            : new Vector3(2.4f, 1.6f, -3f));
        camera.transform.LookAt(subject.transform.position + Vector3.up * .2f);
        var previous = camera.targetTexture;
        var active = RenderTexture.active;
        var rt = new RenderTexture(1280, 720, 24);
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            texture.Apply();
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previous;
            camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            RenderTexture.active = active;
            UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(rt);
        }
    }

    [UnityTest]
    public IEnumerator SoloOwnershipPlacementAndDash()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        var p1 = GameObject.Find("Player1");
        var p2 = GameObject.Find("Player2");
        var i1 = p1.GetComponent(T("HPlayer.InteractionController"));
        var i2 = p2.GetComponent(T("HPlayer.InteractionController"));
        var animator = p1.GetComponent<Animator>();
        var movement = p1.GetComponent(T("ThirdPersonPlayerController"));
        var placementFloor = GameObject.Find("Floor").GetComponent<Collider>();
        // Separate from the heavy crate so each placement is tested on clear floor.
        p1.GetComponent<Rigidbody>().position = new Vector3(5f, .68f, -2.6f);
        p2.GetComponent<Rigidbody>().position = new Vector3(5f, .68f, -.4f);
        Physics.SyncTransforms();
        foreach (var name in new[] { "Box_Small (2)", "Box_Medium (2)", "Box_Big (2)" })
        {
            var box = GameObject.Find(name);
            var liftable = box.GetComponent(T("HInteractions.Liftable"));
            // Stage a nearby pickup; collision-safe carry no longer teleports a
            // distant scene object through intervening level geometry.
            var stagedBody = box.GetComponent<Rigidbody>();
            float bottomOffset = box.transform.position.y - box.GetComponent<Collider>().bounds.min.y;
            stagedBody.position = new Vector3(5f, .51f + bottomOffset, -1.9f);
            Physics.SyncTransforms();
            Call(i1, "PickUpObject", liftable);
            Assert.AreSame(liftable, Prop(i1, "HeldObject"));
            Transform parent = box.transform.parent;
            Call(i2, "PickUpObject", liftable);
            Assert.IsNull(Prop(i2, "HeldObject"), name + " must not be stolen");
            Assert.AreSame(parent, box.transform.parent);
            Assert.AreEqual(1, ((IList)Prop(liftable, "Holders")).Count);
            for (float until = Time.time + .4f; Time.time < until;) yield return null;
            float height = box.transform.position.y;
            Call(i1, "BeginPlaceDown");
            Assert.IsTrue((bool)Prop(i1, "IsPlacingDown"));
            float loweringDistance = height - ((Vector3)Get(i1, "placementTarget")).y;
            float maximumFloorPenetration = 0f;
            // Release happens at the clip's Pickup_Grab event, so sample the lowering phase
            // relative to that authored contact rather than assuming ownership at 1.1 seconds.
            bool sampledLowering = false;
            float placementDeadline = Time.time + 3.5f;
            while (Prop(i1, "HeldObject") != null && Time.time < placementDeadline)
            {
                yield return null;
                foreach (var shape in box.GetComponentsInChildren<Collider>())
                    if (shape.enabled && !shape.isTrigger && Physics.ComputePenetration(shape, shape.transform.position, shape.transform.rotation,
                        placementFloor, placementFloor.transform.position, placementFloor.transform.rotation, out _, out float depth))
                        maximumFloorPenetration = Mathf.Max(maximumFloorPenetration, depth);
                var state = animator.GetCurrentAnimatorStateInfo(0);
                float contact = (float)Get(i1, "placementContactTime");
                if (!sampledLowering && state.IsName("Pickup") &&
                    state.normalizedTime >= contact * .5f && state.normalizedTime < contact * .95f)
                {
                    Assert.AreSame(liftable, Prop(i1, "HeldObject"), "Keep ownership before the placement contact event");
                    if (loweringDistance > .04f)
                        Assert.That(box.transform.position.y, Is.LessThan(height - .02f), name);
                    else
                        Assert.That(box.transform.position.y, Is.LessThanOrEqualTo(height + .01f),
                            "An object already at the floor must not be forced farther down");
                    Capture("Temp/AdaptiveCarryPlaceDown.png", p1);
                    sampledLowering = true;
                }
            }
            Assert.IsTrue(sampledLowering, "Must observe owned object lowering before contact/release");
            Assert.Less(maximumFloorPenetration, .008f, name + " placement path must not cross the floor");
            // The ownership release event precedes the end of the Pickup state.
            for (float until = Time.time + 1.5f; Time.time < until;) yield return null;
            Assert.IsNull(Prop(i1, "HeldObject"));
            Assert.IsFalse((bool)Prop(i1, "IsPickingUp"));
            Assert.IsFalse(box.GetComponent<Rigidbody>().isKinematic);
            Assert.That(new Vector2(box.GetComponent<Rigidbody>().velocity.x, box.GetComponent<Rigidbody>().velocity.z).magnitude,
                Is.LessThan(.15f), "Placement must not throw");
            Assert.That(box.GetComponent<Collider>().bounds.min.y, Is.InRange(.48f, .55f), "Object should rest on demo floor");
            Call(i2, "PickUpObject", liftable);
            Assert.AreSame(liftable, Prop(i2, "HeldObject"), "Released object is available to other player");
            Call(i2, "ReleaseHeldObject");
            // Move the tested box away so the next placement has a clear floor.
            box.GetComponent<Rigidbody>().position += Vector3.left * 4f;
        }
        var thrown = GameObject.Find("Box_Small (2)").GetComponent(T("HInteractions.Liftable"));
        Call(i1, "PickUpObject", thrown);
        Call(i1, "ThrowHeldObject");
        var thrownBody = ((Component)thrown).GetComponent<Rigidbody>();
        Assert.IsNull(Prop(i1, "HeldObject"));
        Assert.That(Vector3.Dot(thrownBody.velocity, p1.transform.forward), Is.EqualTo(10f).Within(.01f));
        thrownBody.position += Vector3.left * 4f;
        thrownBody.velocity = Vector3.zero;
        var cancelled = GameObject.Find("Box_Medium (2)").GetComponent(T("HInteractions.Liftable"));
        Call(i1, "PickUpObject", cancelled);
        Call(i1, "BeginPlaceDown");
        ((Behaviour)i1).enabled = false;
        Assert.IsNull(Prop(i1, "HeldObject"));
        Assert.IsFalse(((Component)cancelled).GetComponent<Rigidbody>().isKinematic);
        Assert.IsFalse((bool)Get(movement, "_movementLocked"));
        ((Behaviour)i1).enabled = true;
        float walkSpeed = (float)Prop(movement, "BaseMoveSpeed");
        Assert.That((float)Call(movement, "DesiredMoveSpeed"), Is.EqualTo(walkSpeed).Within(.001f));
        // Drive the dash once, then allow the Animator to enter and leave its pose state.
        ((Behaviour)movement).enabled = false;
        Assert.IsTrue((bool)Call(movement, "TryStartDash"));
        Assert.IsFalse((bool)Call(movement, "TryStartDash"), "Dash has a cooldown");
        Call(movement, "FixedUpdate");
        Assert.That(new Vector2(p1.GetComponent<Rigidbody>().velocity.x, p1.GetComponent<Rigidbody>().velocity.z).magnitude,
            Is.EqualTo((float)Get(movement, "_dashSpeed")).Within(.01f));
        for (float until = Time.time + .2f; Time.time < until;)
        {
            Call(movement, "UpdateLocomotionAnimation", (float)Get(movement, "_dashSpeed"), Time.deltaTime);
            yield return null;
        }
        Assert.IsTrue(animator.GetBool("isDashing"));
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Dash"), "Dash state must be reached");
        Capture("Temp/AdaptiveCarryDash.png", p1);
        Set(movement, "_dashRemaining", 0f);
        Call(movement, "UpdateLocomotionAnimation", 0f, 1f / 60f);
        Assert.That((float)Get(movement, "_animatedSpeed"), Is.GreaterThan(0f), "Animation decelerates instead of snapping");
        Assert.IsFalse(animator.GetBool("isWalking"), "Walking should stop when measured speed reaches zero");
        Assert.IsFalse(animator.GetBool("isDashing"), "Dash pose should end with the dash");
        for (float until = Time.time + 3f; Time.time < until;)
        {
            Call(movement, "UpdateLocomotionAnimation", 0f, Time.deltaTime);
            yield return null;
        }
        Assert.IsFalse(animator.GetBool("isWalking"));
        Assert.IsFalse(animator.GetBool("isDashing"));
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle 0"));
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SingleCarryContactAcrossSizesAndWeights()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        var player = GameObject.Find("Player1");
        var interaction = player.GetComponent(T("HPlayer.InteractionController"));
        var lean = player.GetComponent(T("TA.AdaptiveAnimation.HeldObjectBodyLeanController"));
        var grip = player.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectGripController"));
        Assert.IsNull(player.GetComponent(T("TA.AdaptiveAnimation.AdaptiveAnimationManager")),
            "The source demo verifies the legacy consumer path without an opted-in context manager");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "CarryContactMatrixFloor";
        floor.transform.position = new Vector3(30f, .4f, 30f);
        floor.transform.localScale = new Vector3(20f, .2f, 20f);
        player.transform.SetPositionAndRotation(new Vector3(30f, .68f, 28f), Quaternion.identity);
        player.GetComponent<Rigidbody>().position = player.transform.position;
        player.GetComponent<Rigidbody>().rotation = player.transform.rotation;
        Physics.SyncTransforms();
        var weights = new[] { 10f, 30f, 50f, 100f };
        var angles = new[] { 8f, 16f, 24f, 24f };
        foreach (string name in new[] { "Box_Small (2)", "Box_Medium (2)", "Box_Big (2)" })
        {
            var box = GameObject.Find(name);
            var liftable = box.GetComponent(T("HInteractions.Liftable"));
            var load = box.GetComponent(T("TA.AdaptiveAnimation.CarryAnimationLoad"));
            var provider = box.GetComponent(T("TA.AdaptiveAnimation.GripPointProvider"));
            Assert.IsNotNull(load, name + " must expose animation load");
            Assert.IsNotNull(box.GetComponent<Rigidbody>(), name + " requires Rigidbody before pickup");
            Assert.IsNotNull(Prop(liftable, "Rigidbody"), name + " requires cached Rigidbody before pickup");
            // Keep this contact matrix independent of intervening demo props.
            // A private pickup from the saved 5-9 m distance must not bypass
            // the same collision guard used by real nearby pickup.
            var itemBody = box.GetComponent<Rigidbody>();
            box.transform.SetPositionAndRotation(new Vector3(30f, 1f, 28.7f), Quaternion.identity);
            itemBody.position = box.transform.position;
            itemBody.rotation = box.transform.rotation;
            Physics.SyncTransforms();
            itemBody.position += Vector3.up * (.51f - box.GetComponent<Collider>().bounds.min.y);
            box.transform.position = itemBody.position;
            Physics.SyncTransforms();
            Assert.Less(Vector3.ProjectOnPlane(box.transform.position - player.transform.position, Vector3.up).magnitude, 1f,
                "Contact matrix pickup must start nearby on supported ground");
            Call(interaction, "PickUpObject", liftable);
            Assert.AreSame(liftable, Prop(interaction, "HeldObject"));
            for (int i = 0; i < weights.Length; i++)
            {
                Set(load, "weightKg", weights[i]);
                // Allow the actual Update/Animator/RigBuilder pipeline to settle after each load change.
                for (float until = Time.time + 1f; Time.time < until;) yield return null;
                string scenario = name + " / " + weights[i] + "kg";
                Assert.That((float)Get(lean, "currentStylizedAngle"),
                    Is.EqualTo(angles[i]).Within(.5f), scenario + " calibrated body lean");
                // Sample consecutive evaluated frames: checking target distance alone would miss a faded-out IK rig.
                for (int frame = 0; frame < 10; frame++)
                {
                    yield return null;
                    Assert.That((float)Get(grip, "currentWeight"), Is.GreaterThan(.99f), scenario + " IK must remain engaged");
                    var points = new object[] { interaction, Vector3.zero, Vector3.zero };
                    var method = provider.GetType().GetMethod("TryGetGripPoints", new[] {
                        T("HPlayer.InteractionController"), typeof(Vector3).MakeByRefType(), typeof(Vector3).MakeByRefType() });
                    Assert.IsTrue((bool)method.Invoke(provider, points), scenario + " valid grip points");
                    bool swap = (bool)Get(grip, "swapHands");
                    var left = Get(Get(grip, "leftArmIK"), "m_Data");
                    var right = Get(Get(grip, "rightArmIK"), "m_Data");
                    Assert.That(Vector3.Distance(((Transform)Prop(left, "tip")).position, (Vector3)points[swap ? 2 : 1]),
                        Is.LessThan(.035f), scenario + " left hand to object contact");
                    Assert.That(Vector3.Distance(((Transform)Prop(right, "tip")).position, (Vector3)points[swap ? 1 : 2]),
                        Is.LessThan(.035f), scenario + " right hand to object contact");
                    var visuals = (GameObject)Get(player.GetComponent(T("TA.AdaptiveAnimation.CharacterModelSlot")), "generatedVisuals");
                    foreach (var bone in visuals.GetComponentsInChildren<Transform>())
                    {
                        if (bone.name == "hand.L") Assert.That(Vector3.Distance(bone.position, (Vector3)points[swap ? 2 : 1]), Is.LessThan(.04f), scenario + " visible left hand contact");
                        if (bone.name == "hand.R") Assert.That(Vector3.Distance(bone.position, (Vector3)points[swap ? 1 : 2]), Is.LessThan(.04f), scenario + " visible right hand contact");
                    }
                    AssertVisibleCarryClearance(player, box, scenario);
                    AssertGripOnVisibleBounds(box, (Vector3)points[1], scenario + " left grip surface");
                    AssertGripOnVisibleBounds(box, (Vector3)points[2], scenario + " right grip surface");
                }
                Capture("Temp/CarryContact_" + box.name.Replace(' ', '_') + "_" + weights[i] + "kg.png", player);
                // Measure the evaluated chest against the same carry animation with only its lean rig disabled.
                // This proves visible backward rotation instead of merely trusting the requested angle.
                var constraint = Get(lean, "bodyLean");
                var weightProperty = constraint.GetType().GetProperty("weight");
                float savedWeight = (float)weightProperty.GetValue(constraint);
                weightProperty.SetValue(constraint, 0f);
                yield return null;
                yield return null;
                Quaternion neutralChest = Chest(player).rotation;
                weightProperty.SetValue(constraint, savedWeight);
                yield return null;
                yield return null;
                Quaternion delta = Chest(player).rotation * Quaternion.Inverse(neutralChest);
                delta.ToAngleAxis(out float chestAngle, out Vector3 chestAxis);
                if (chestAngle > 180f) chestAngle -= 360f;
                float backward = Vector3.Dot(chestAxis * chestAngle, Vector3.Cross(Vector3.up, -player.transform.forward));
                Assert.That(backward, Is.GreaterThan(angles[i] * .7f), scenario + " chest must visibly lean backward");
            }
            Call(interaction, "ReleaseHeldObject");
            for (float until = Time.time + .5f; Time.time < until;) yield return null;
            Assert.That((float)Get(grip, "currentWeight"), Is.LessThan(.01f), "Released grip must fade out");
            box.GetComponent<Rigidbody>().position += Vector3.left * 4f;
        }
        yield return new ExitPlayMode();
    }

    static void AssertVisibleCarryClearance(GameObject player, GameObject item, string scenario)
    {
        var provider = item.GetComponent(T("TA.AdaptiveAnimation.BoxGripProvider"));
        Assert.IsTrue((bool)Prop(provider, "HasSurfaceGeometry"), scenario + " uses readable visible mesh");
        var slot = player.GetComponent(T("TA.AdaptiveAnimation.CharacterModelSlot"));
        var visuals = (GameObject)Get(slot, "generatedVisuals");
        var bones = visuals.GetComponentsInChildren<Transform>();
        Transform torso = bones.First(b => b.name == "spine.003");
        Transform pelvis = bones.First(b => b.name == "spine.001");
        Transform head = bones.First(b => b.name == "spine.006");
        Transform headEnd = bones.First(b => b.name == "spine.006_end");
        var clearance = provider.GetType().GetMethod("GetSurfaceClearance");
        int proxyIndex = 0;
        foreach (var proxy in new[] {
            new { point = torso.position, radius = .08f },
            new { point = pelvis.position, radius = .14f },
            new { point = Vector3.Lerp(head.position, headEnd.position, .4f), radius = .35f } })
            Assert.That(((Vector3)clearance.Invoke(provider, new object[] { proxy.point, proxy.radius, Vector3.zero })).magnitude,
                Is.LessThan(.015f), scenario + " visible body/head clearance proxy " + proxyIndex++);
    }

    static void AssertGripOnVisibleBounds(GameObject item, Vector3 point, string scenario)
    {
        bool onSurface = false;
        foreach (var filter in item.GetComponentsInChildren<MeshFilter>())
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            var probe = new GameObject("Grip surface probe");
            probe.transform.SetParent(filter.transform, false);
            var collider = probe.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            Physics.SyncTransforms();
            Vector3 forward = item.transform.forward;
            onSurface |= collider.Raycast(new Ray(point - forward * .03f, forward), out var hit, .06f) &&
                Vector3.Distance(hit.point, point) < .025f;
            UnityEngine.Object.DestroyImmediate(probe);
            if (onSurface) break;
        }
        Assert.IsTrue(onSurface, scenario + " must lie on an enabled visible mesh");
    }

    [UnityTest]
    public IEnumerator HeavyPickupAnticipatesThenLeansBackward()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        var player = GameObject.Find("Player1");
        var interaction = player.GetComponent(T("HPlayer.InteractionController"));
        var lean = player.GetComponent(T("TA.AdaptiveAnimation.HeldObjectBodyLeanController"));
        var box = GameObject.Find("Box_Medium (2)");
        var liftable = box.GetComponent(T("HInteractions.Liftable"));
        Set(box.GetComponent(T("TA.AdaptiveAnimation.CarryAnimationLoad")), "weightKg", 50f);
        player.GetComponent<Rigidbody>().position = new Vector3(5f, .68f, -2.6f);
        box.GetComponent<Rigidbody>().position = new Vector3(5f, .9f, -1.9f);
        Physics.SyncTransforms();
        yield return null;
        yield return null;
        Quaternion initialChest = Chest(player).rotation;
        // BeginPickup starts the real Animator state and contact event; do not call PickUpObject directly.
        Call(interaction, "BeginPickup", liftable);
        Assert.AreSame(liftable, Prop(interaction, "PendingPickupObject"));
        bool sawAnticipation = false;
        float deadline = Time.time + 3f;
        while (Prop(interaction, "HeldObject") == null && Time.time < deadline)
        {
            yield return null;
            if (Prop(interaction, "PendingPickupObject") == null) continue;
            if ((float)Get(lean, "currentStylizedAngle") >= -3f) continue;
            Quaternion delta = Chest(player).rotation * Quaternion.Inverse(initialChest);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            float forward = Vector3.Dot(axis * angle, Vector3.Cross(Vector3.up, player.transform.forward));
            if (forward <= 3f) continue;
            if (!sawAnticipation) Capture("Temp/HeavyPickupAnticipation.png", player);
            sawAnticipation = true;
        }
        Assert.IsTrue(sawAnticipation, "Heavy pickup must visibly anticipate forward before the contact event");
        Assert.AreSame(liftable, Prop(interaction, "HeldObject"), "Actual animation contact must acquire the object");
        for (float until = Time.time + 2f; Time.time < until;) yield return null;
        Assert.That((float)Get(lean, "currentStylizedAngle"), Is.EqualTo(24f).Within(.5f));
        Assert.That((float)Get(lean, "currentHeadLookUp"), Is.GreaterThan(3f), "Heavy carry must visibly lift the head");
        AssertHandContact(player);
        AssertVisibleCarryClearance(player, box, "heavy pickup settled carry");
        Capture("Temp/HeavyPickupBackwardCarry.png", player);
        Call(interaction, "ReleaseHeldObject");
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SingleCarryWeightAndRelease()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        var player = GameObject.Find("Player1");
        var interaction = player.GetComponent(T("HPlayer.InteractionController"));
        var movement = player.GetComponent(T("ThirdPersonPlayerController"));
        var box = GameObject.Find("Box_Medium (2)");
        var liftable = box.GetComponent(T("HInteractions.Liftable"));
        var load = box.GetComponent(T("TA.AdaptiveAnimation.CarryAnimationLoad"));
        Call(interaction, "PickUpObject", liftable);
        Assert.AreSame(liftable, interaction.GetType().GetProperty("HeldObject").GetValue(interaction));
        Assert.IsFalse((bool)Call(movement, "TryStartDash"), "Carrying an object blocks dash");
        var weights = new[] { 10f, 20f, 30f, 50f };
        var multipliers = new[] { .85f, .75f, .65f, .4f };
        for (int i = 0; i < weights.Length; i++)
        {
            Set(load, "weightKg", weights[i]);
            Call(interaction, "FixedUpdate");
            Assert.That((float)Get(movement, "_speedModifier"), Is.EqualTo(multipliers[i]).Within(.0001f));
            Assert.That((float)Call(movement, "DesiredMoveSpeed"),
                Is.EqualTo((float)Prop(movement, "BaseMoveSpeed") * multipliers[i]).Within(.0001f), "Carrying uses the weight penalty");
            Debug.Log("CARRY TEST: " + weights[i] + "kg -> " +
                ((float)Get(movement, "_moveSpeed") * multipliers[i]) + " units/s commanded");
        }
        Call(interaction, "ReleaseHeldObject");
        Assert.IsNull(interaction.GetType().GetProperty("HeldObject").GetValue(interaction));
        Assert.That((float)Get(movement, "_speedModifier"), Is.EqualTo(1f));
        Assert.IsNull(box.transform.parent);
        yield return new ExitPlayMode();
    }
}
