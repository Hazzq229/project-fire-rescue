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
        Assert.AreEqual(1, Convert.ToInt32(Get(p2.GetComponent(T("HPlayer.LocalPlayerInput")), "profile")));
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
            Assert.That(heavyRb.position.x - start.x, Is.EqualTo(.252f * 25 * Time.fixedDeltaTime).Within(.01f));
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
        Assert.That((float)Prop(coop, "SpeedPenalty"), Is.EqualTo(.15f).Within(.0001f));
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
            Assert.That(rb.position.x - start.x, Is.EqualTo(2.55f * 25 * Time.fixedDeltaTime).Within(.01f));
            Assert.That(Vector3.Distance(separation, p1.GetComponent<Rigidbody>().position - p2.GetComponent<Rigidbody>().position), Is.LessThan(.01f));
            start = rb.position;
            Call(coop, "MoveTogether", Vector2.right, Vector2.left);
            Physics.Simulate(Time.fixedDeltaTime);
            Assert.That(Vector3.Distance(start, rb.position), Is.LessThan(.001f));
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
        camera.transform.position = subject.transform.position + new Vector3(2.4f, 1.6f, -3f);
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
    public IEnumerator SoloOwnershipPlacementAndRunning()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        var p1 = GameObject.Find("Player1");
        var p2 = GameObject.Find("Player2");
        var i1 = p1.GetComponent(T("HPlayer.InteractionController"));
        var i2 = p2.GetComponent(T("HPlayer.InteractionController"));
        var animator = p1.GetComponent<Animator>();
        var movement = p1.GetComponent(T("ThirdPersonPlayerController"));
        // Separate from the heavy crate so each placement is tested on clear floor.
        p1.GetComponent<Rigidbody>().position = new Vector3(5f, .68f, -2.6f);
        p2.GetComponent<Rigidbody>().position = new Vector3(5f, .68f, -.4f);
        Physics.SyncTransforms();
        foreach (var name in new[] { "Box_Small (2)", "Box_Medium (2)", "Box_Big (2)" })
        {
            var box = GameObject.Find(name);
            var liftable = box.GetComponent(T("HInteractions.Liftable"));
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
            for (float until = Time.time + 1.1f; Time.time < until;) yield return null;
            Assert.AreSame(liftable, Prop(i1, "HeldObject"), "Keep ownership during lowering");
            Assert.That(box.transform.position.y, Is.LessThan(height - .02f));
            Capture("Temp/AdaptiveCarryPlaceDown.png", p1);
            for (float until = Time.time + 2f; Time.time < until;) yield return null;
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
        Assert.That((float)Call(movement, "DesiredMoveSpeed", true), Is.EqualTo(4.95f).Within(.001f));
        // Supply measured speeds across real frames so the Animator state machine advances normally.
        ((Behaviour)movement).enabled = false;
        for (float until = Time.time + 2f; Time.time < until;)
        {
            Call(movement, "UpdateLocomotionAnimation", 4.95f, Time.deltaTime);
            yield return null;
        }
        Assert.IsTrue(animator.GetBool("isRunning"));
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Running"), "Running state must be reached, hash=" + animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
        Assert.That(animator.GetFloat("LocomotionRate"), Is.InRange(.95f, 1.05f));
        Capture("Temp/AdaptiveCarryRunning.png", p1);
        Call(movement, "UpdateLocomotionAnimation", 0f, 1f / 60f);
        Assert.That((float)Get(movement, "_animatedSpeed"), Is.GreaterThan(0f), "Animation decelerates instead of snapping");
        for (float until = Time.time + 3f; Time.time < until;)
        {
            Call(movement, "UpdateLocomotionAnimation", 0f, Time.deltaTime);
            yield return null;
        }
        Assert.IsFalse(animator.GetBool("isWalking"));
        Assert.IsFalse(animator.GetBool("isRunning"));
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle 0"));
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
        var weights = new[] { 10f, 20f, 30f, 50f };
        var multipliers = new[] { 1f, .925f, .85f, .7f };
        for (int i = 0; i < weights.Length; i++)
        {
            Set(load, "weightKg", weights[i]);
            Call(interaction, "FixedUpdate");
            Assert.That((float)Get(movement, "_speedModifier"), Is.EqualTo(multipliers[i]).Within(.0001f));
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
