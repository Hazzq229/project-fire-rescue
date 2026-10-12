using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class CooperativeHoseTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static object Call(object o, string method, params object[] args) => o.GetType().GetMethods(Flags)
        .First(m => m.Name == method && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v)).Invoke(o, args);
    static object P(object o, string p) => o.GetType().GetProperty(p).GetValue(o);
    static object F(object o, string f)
    {
        Assert.IsNotNull(o, "Reading field " + f);
        var field = o.GetType().GetField(f, Flags);
        Assert.IsNotNull(field, "Missing field " + o.GetType().FullName + "." + f);
        return field.GetValue(o);
    }
    static Component C(GameObject o, string type) => o.GetComponent(T(type));
    static Transform Foot(GameObject player) => player.GetComponentsInChildren<Transform>().First(t => t.name == "foot.L");
    internal static void CheckHandContact(Component grip)
    {
        foreach (string name in new[] { "leftArmIK", "rightArmIK" })
        {
            var constraint = (Component)F(grip, name);
            Type type = constraint.GetType(); FieldInfo field = null;
            while (type != null && field == null) { field = type.GetField("m_Data", Flags); type = type.BaseType; }
            var data = field.GetValue(constraint);
            var tip = (Transform)P(data, "tip"); var target = (Transform)P(data, "target");
            Assert.Less(Vector3.Distance(tip.position, target.position), .035f, "Actual hand contact: " + name);
        }
    }
    static void Position(GameObject p, Vector3 position)
    {
        p.GetComponent<Rigidbody>().position = position;
        p.GetComponent<Rigidbody>().rotation = Quaternion.identity;
        p.GetComponent<Rigidbody>().velocity = Vector3.zero;
        Physics.SyncTransforms();
    }
    internal static void Capture(string path, Vector3 point)
    {
        var go = new GameObject("HoseTestCamera");
        var camera = go.AddComponent<Camera>();
        go.transform.position = point + new Vector3(4, 3, -5);
        go.transform.LookAt(point);
        camera.fieldOfView = 38;
        var rt = new RenderTexture(1280, 720, 24);
        var old = RenderTexture.active;
        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        RenderTexture.active = old; camera.targetTexture = null;
        UnityEngine.Object.Destroy(go); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(texture);
    }

    [UnityTest]
    public IEnumerator PickupEventsAndAdaptiveHandContact()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity");
        yield return new EnterPlayMode();
        var p1 = GameObject.Find("Player1"); var p2 = GameObject.Find("Player2");
        var a = C(p1, "HPlayer.InteractionController"); var b = C(p2, "HPlayer.InteractionController");
        var front = GameObject.Find("HoseNozzle"); var rear = GameObject.Find("Hose_RearGrip");
        var hose = C(front, "TA.AdaptiveAnimation.CooperativeFireHose");
        for (float until = Time.time + .3f; Time.time < until;) yield return null;
        float originalFootY = Foot(p1).position.y;
        Position(p1, new Vector3(front.transform.position.x, .39f, front.transform.position.z - .45f));
        Position(p2, p1.transform.position - Vector3.forward * 1.05f);
        for (float until = Time.time + .3f; Time.time < until;) yield return null;
        Call(a, "BeginInteraction");
        Assert.IsTrue((bool)P(a, "IsPickingUp"), "Nozzle must be reachable through the normal grab trigger");
        for (float until = Time.time + 4f; (bool)P(a, "IsPickingUp") && Time.time < until;) yield return null;
        Assert.AreSame(C(front, "TA.AdaptiveAnimation.CooperativeHoseGrip"), P(a, "HeldObject"), "Actual Pickup_Grab event attaches nozzle");
        Position(p2, new Vector3(rear.transform.position.x, .39f, rear.transform.position.z - .42f));
        for (float until = Time.time + .3f; Time.time < until;) yield return null;
        Call(b, "BeginInteraction");
        for (float until = Time.time + 4f; (bool)P(b, "IsPickingUp") && Time.time < until;) yield return null;
        Assert.AreSame(C(rear, "TA.AdaptiveAnimation.CooperativeHoseGrip"), P(b, "HeldObject"), "Actual Pickup_Grab event attaches rear station");
        for (float until = Time.time + 1f; Time.time < until;) yield return null;
        Assert.IsTrue((bool)P(hose, "ReadyToSpray"));
        Capture("Temp/CooperativeHose-Pickup.png", p1.transform.position + Vector3.up * .4f);
        foreach (var player in new[] { p1, p2 })
        {
            var grip = player.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectGripController"));
            Debug.Log("HOSE hand status " + player.name + ": " + F(grip, "status") + " weight=" + F(grip, "currentWeight") +
                " left=" + F(grip, "leftDistance") + "/" + F(grip, "leftMaximumReach") +
                " right=" + F(grip, "rightDistance") + "/" + F(grip, "rightMaximumReach"));
            Assert.Greater((float)F(grip, "currentWeight"), .95f, "Both hands must reach the hose: " + player.name);
            CheckHandContact(grip);
            Assert.AreEqual(Animator.StringToHash("HoseHold"), player.GetComponent<Animator>().GetCurrentAnimatorStateInfo(2).shortNameHash);
        }
        Assert.That(Foot(p1).position.y, Is.EqualTo(originalFootY).Within(.025f), "Hose pose must not rotate/lift the feet");
        foreach (float direction in new[] { 1f, -1f })
        {
            Call(hose, "ApplyAim", a, new Vector2(0, direction), 2f);
            for (float until = Time.time + .5f; Time.time < until;) yield return null;
            CheckHandContact(p1.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectGripController")));
        }
        Capture("Temp/CooperativeHose-Pickup.png", p1.transform.position + Vector3.up * .4f);
        Call(a, "EndInteraction"); Call(a, "BeginInteraction");
        Assert.IsNull(P(a, "HeldObject")); Assert.IsNull(P(b, "HeldObject"));
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator PressureRolesReelExtinguishingAndKnockback()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity");
        yield return new EnterPlayMode();
        var p1 = GameObject.Find("Player1"); var p2 = GameObject.Find("Player2");
        Assert.NotNull(p1); Assert.NotNull(p2);
        var a = C(p1, "HPlayer.InteractionController"); var b = C(p2, "HPlayer.InteractionController");
        var front = GameObject.Find("HoseNozzle"); var rear = GameObject.Find("Hose_RearGrip");
        var hose = C(front, "TA.AdaptiveAnimation.CooperativeFireHose");
        var fg = C(front, "TA.AdaptiveAnimation.CooperativeHoseGrip");
        var rg = C(rear, "TA.AdaptiveAnimation.CooperativeHoseGrip");
        var water = C(front, "HGame.Objects.HoseWaterController");
        Debug.Log("HOSE phase setup: " + hose + " / " + water);
        Position(p1, new Vector3(9, .39f, -22.22f));
        Position(p2, new Vector3(9, .39f, -23.27f));
        Call(a, "PickUpObject", fg);
        Call(hose, "StepPressure", true, .1f);
        Assert.AreEqual(0f, P(hose, "Pressure"), "One player cannot spray");
        Call(water, "ToggleShooting");
        Assert.IsFalse((bool)P(water, "IsShooting"), "Legacy toggle cannot bypass the second role");
        Call(b, "PickUpObject", fg);
        Assert.IsNull(P(b, "HeldObject"), "Nozzle cannot be stolen");
        Call(b, "PickUpObject", rg);
        Physics.SyncTransforms();
        Assert.IsTrue((bool)P(hose, "ReadyToSpray"));
        Debug.Log("HOSE phase team ready");
        for (int i = 0; i < 35; i++) Call(hose, "StepPressure", true, .1f);
        Assert.That((float)P(hose, "Pressure"), Is.EqualTo(.7f).Within(.002f));
        Assert.IsTrue((bool)P(water, "IsShooting"));
        var vfx = (Component)F(water, "waterFX");
        float highSpawn = (float)Call(vfx, "GetFloat", "SpawnRate");
        for (int i = 0; i < 6; i++) Call(hose, "StepPressure", false, .1f);
        Assert.That((float)P(hose, "Pressure"), Is.InRange(.42f, .44f));
        Assert.Less((float)Call(vfx, "GetFloat", "SpawnRate"), highSpawn, "Release must visibly reduce flow");
        for (int i = 0; i < 20; i++) Call(hose, "StepPressure", false, .1f);
        Assert.IsFalse((bool)P(water, "IsShooting"));
        Debug.Log("HOSE phase pressure decay passed");
        Call(hose, "ToggleReel", a, 1);
        Assert.AreEqual(0, P(hose, "ReelDirection"), "Only rear can change length");
        Call(hose, "ToggleReel", b, 1);
        Call(hose, "StepReel", 2f);
        Assert.That((float)P(hose, "HoseLength"), Is.EqualTo(9f).Within(.01f));
        Debug.Log("HOSE phase reel passed");
        var cable = front.GetComponentInParent(T("HPhysic.PhysicCable"));
        Assert.IsNotNull(cable, "Scene hose must retain cable reference");
        Debug.Log("HOSE reel length=" + P(hose, "HoseLength") + " joints=" + cable.GetComponentsInChildren<SpringJoint>().Length);
        // Avoid capturing iterator locals across EnterPlayMode's domain reload.
        foreach (var joint in cable.GetComponentsInChildren<SpringJoint>())
            if (joint.connectedBody && joint.connectedBody.transform.IsChildOf(cable.transform))
                Assert.That(joint.maxDistance, Is.EqualTo(.9f).Within(.001f), "Physical cable joints must change length");
        Call(hose, "ToggleReel", b, 1);
        Assert.AreEqual(0, P(hose, "ReelDirection"), "Second press stops the toggle");
        Call(hose, "ToggleReel", b, -1); Call(hose, "StepReel", .5f);
        Assert.Less((float)P(hose, "HoseLength"), 9f);
        Call(hose, "ToggleReel", b, 1); Call(hose, "StepReel", 100f);
        Assert.AreEqual(18f, P(hose, "HoseLength"));
        Assert.AreEqual(0, P(hose, "ReelDirection"));
        Vector3 aim = (Vector3)P(hose, "AimDirection");
        Call(hose, "ApplyAim", b, Vector2.one, .2f);
        Assert.AreEqual(aim, P(hose, "AimDirection"), "Rear must not steer the nozzle");
        Call(hose, "ApplyAim", a, Vector2.up, .2f);
        Assert.Greater(((Vector3)P(hose, "AimDirection")).y, .1f);
        Call(hose, "ApplyAim", a, Vector2.down, .2f);
        Call(hose, "AttachNozzle"); Physics.SyncTransforms();

        // Exercise the existing Fire.TryExtinguish path, including an obstruction and a non-fire trigger.
        var origin = (Transform)F(water, "raycastOrigin");
        var fireGo = new GameObject("HoseTargetFire");
        fireGo.transform.position = origin.position + origin.forward * 1.5f - Vector3.up * .35f;
        var fireCollider = fireGo.AddComponent<BoxCollider>(); fireCollider.size = Vector3.one; fireCollider.isTrigger = true;
        var fire = fireGo.AddComponent(T("Fire"));
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.transform.position = origin.position + origin.forward * .6f;
        blocker.transform.localScale = new Vector3(1, 1, .1f);
        Physics.SyncTransforms();
        for (int i = 0; i < 40; i++) Call(hose, "StepPressure", true, .1f);
        Call(water, "HandleFireExtinguishing");
        Assert.AreEqual(1f, Call(fire, "GetIntensity"), "Wall blocks the stream");
        blocker.GetComponent<Collider>().isTrigger = true; Physics.SyncTransforms();
        for (int i = 0; i < 100; i++) Call(water, "HandleFireExtinguishing");
        Assert.LessOrEqual((float)Call(fire, "GetIntensity"), 0f, "Existing fire extinguishes; non-fire triggers do not obstruct");
        UnityEngine.Object.Destroy(blocker); UnityEngine.Object.Destroy(fireGo);
        for (float until = Time.time + .8f; Time.time < until;) yield return null;
        Capture("Temp/CooperativeHose-Team.png", p1.transform.position + Vector3.up * .4f);

        for (int i = 0; i < 80 && (int)P(hose, "BurstCount") == 0; i++) Call(hose, "StepPressure", true, .1f);
        Assert.AreEqual(1, P(hose, "BurstCount"));
        Assert.IsNull(P(a, "HeldObject")); Assert.IsNull(P(b, "HeldObject"));
        Assert.IsFalse((bool)P(water, "IsShooting"));
        var mover = C(p1, "ThirdPersonPlayerController");
        Assert.IsTrue((bool)P(mover, "IsKnockedBack"));
        Vector3 before = p1.transform.position;
        for (float until = Time.time + 1.3f; Time.time < until;) yield return null;
        Assert.Greater(Vector3.Distance(before, p1.transform.position), .2f, "Front player must actually recoil");
        Assert.IsFalse((bool)P(mover, "IsKnockedBack"), "Movement control must recover");

        Position(p2, new Vector3(9, .39f, -22.22f)); Position(p1, new Vector3(9, .39f, -23.27f));
        Call(b, "PickUpObject", fg); Call(a, "PickUpObject", rg); Physics.SyncTransforms();
        Assert.AreSame(b, P(hose, "FrontPlayer")); Assert.AreSame(a, P(hose, "RearPlayer"));
        Assert.IsTrue((bool)P(hose, "HasTeam"), "Roles must work when players swap");
        ((Behaviour)a).enabled = false;
        Assert.IsFalse((bool)P(hose, "HasTeam")); Assert.IsNull(P(b, "HeldObject"));
        Assert.IsNull(P(C(p2, "ThirdPersonPlayerController"), "CooperativeHose"));
        yield return new ExitPlayMode();
    }
}
