using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public class GameloopCarryMotionTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static Component C(GameObject go, string type) => go.GetComponent(T(type));
    static object P(object o, string name) => o.GetType().GetProperty(name).GetValue(o);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethods(Flags)
        .First(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v)).Invoke(o, args);
    static void Keys(params string[] keys) => typeof(CooperativeHoseTests).GetMethod("KeyboardState", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { keys });
    static void Position(GameObject go, Vector3 position, Quaternion rotation)
    {
        go.transform.SetPositionAndRotation(position, rotation);
        var body = go.GetComponent<Rigidbody>(); body.position = position; body.rotation = rotation;
        if (!body.isKinematic) body.velocity = Vector3.zero;
        Physics.SyncTransforms();
    }
    static IEnumerator Wait(float seconds) { for (float end = Time.time + seconds; Time.time < end;) yield return null; }
    static Component Actor(string name) => C(GameObject.Find(name), "HPlayer.InteractionController");
    static Component Team() => C(GameObject.Find("Carry_Props_Table_01"), "TA.AdaptiveAnimation.CooperativeCarryObject");
    static void TeamSetup(bool pair)
    {
        var team = Team(); var table = team.gameObject;
        Position(table, new Vector3(30f, .75f, 30f), Quaternion.identity);
        var role = T("TA.AdaptiveAnimation.CarryRole");
        Position(GameObject.Find("Player1"), (Vector3)Call(team, "GetStance", Enum.Parse(role, "Front"), .35f), Quaternion.Euler(0, 180, 0));
        Position(GameObject.Find("Player2"), pair ? (Vector3)Call(team, "GetStance", Enum.Parse(role, "Back"), .35f) : new Vector3(35, .35f, 30), Quaternion.identity);
        Call(Actor("Player1"), "PickUpObject", team);
        if (pair) Call(Actor("Player2"), "PickUpObject", team);
    }
    static void Contacts(string name) => typeof(GameloopMigrationTests).GetMethod("VisiblePropContacts", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { GameObject.Find(name) });
    static float Penetration(GameObject go, Collider wall)
    {
        float maximum = 0f;
        foreach (var shape in go.GetComponentsInChildren<Collider>())
            if (shape.enabled && !shape.isTrigger && Physics.ComputePenetration(shape, shape.transform.position, shape.transform.rotation,
                wall, wall.transform.position, wall.transform.rotation, out _, out float depth)) maximum = Mathf.Max(maximum, depth);
        return maximum;
    }
    static BoxCollider Wall(Vector3 position, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "CarryMotionTestWall";
        go.transform.position = position; go.transform.localScale = size; Physics.SyncTransforms(); return go.GetComponent<BoxCollider>();
    }
    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying)
        {
            typeof(CooperativeHoseTests).GetMethod("ResetKeyboard", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            yield return new ExitPlayMode();
        }
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
    }
    [UnityTest]
    public IEnumerator HeavySoloAndTeamGaitMatchDisplacement()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity"); yield return new EnterPlayMode();
        Keys(); yield return Wait(.3f); TeamSetup(false); yield return Wait(1f);
        var player = GameObject.Find("Player1"); var animator = player.GetComponent<Animator>();
        Keys("D"); yield return Wait(.5f); Vector3 before = player.GetComponent<Rigidbody>().position;
        yield return Wait(1f); float speed = Vector3.Distance(before, player.GetComponent<Rigidbody>().position);
        Debug.Log("MOTION heavy solo actual=" + speed + " rate=" + animator.GetFloat("LocomotionRate"));
        Assert.That(speed, Is.InRange(.025f, .09f));
        Assert.IsTrue(animator.GetBool("isWalking"), "Very slow heavy movement still needs a gait");
        Assert.That(Mathf.Abs(animator.GetFloat("LocomotionRate")), Is.EqualTo(speed).Within(.015f));
        Keys(); yield return Wait(.5f); Assert.IsFalse(animator.GetBool("isWalking"));
        Call(Actor("Player1"), "ReleaseHeldObject"); TeamSetup(true); yield return Wait(2f);
        Assert.AreEqual("Carrying", P(Team(), "State").ToString());
        Keys("W", "UpArrow"); yield return Wait(.8f);
        Assert.Less(animator.GetFloat("LocomotionRate"), -.1f, "Front operator steps backward during forward group travel");
        Assert.Greater(GameObject.Find("Player2").GetComponent<Animator>().GetFloat("LocomotionRate"), .1f);
        Keys("D", "RightArrow"); yield return Wait(.6f);
        var visual = (GameObject)C(player, "TA.AdaptiveAnimation.CharacterModelSlot").GetType().GetField("generatedVisuals").GetValue(C(player, "TA.AdaptiveAnimation.CharacterModelSlot"));
        var mirror = visual.GetComponent(T("TA.AdaptiveAnimation.CharacterModelAnimatorMirror"));
        Assert.Greater(Mathf.Abs((float)mirror.GetType().GetField("carryStrideYaw", Flags).GetValue(mirror)), 60f);
        Keys(); yield return Wait(.4f);
        Vector3 anchorBefore = GameObject.Find("Player2").GetComponent<Rigidbody>().position;
        Keys("D"); yield return Wait(.6f); Keys();
        Assert.Less(Vector3.Distance(anchorBefore, GameObject.Find("Player2").GetComponent<Rigidbody>().position), .03f);
        Assert.IsFalse(GameObject.Find("Player2").GetComponent<Animator>().GetBool("isWalking"), "Stationary anchor must not walk");
        yield return Wait(.4f);
        before = player.GetComponent<Rigidbody>().position; Keys("L"); yield return Wait(.8f);
        float turnSpeed = Vector3.Distance(before, player.GetComponent<Rigidbody>().position) / .8f;
        Debug.Log("MOTION idle turn actual=" + turnSpeed + " rate=" + animator.GetFloat("LocomotionRate"));
        Assert.Greater(turnSpeed, .2f);
        Assert.IsTrue(animator.GetBool("isWalking"));
        Assert.That(Mathf.Abs(animator.GetFloat("LocomotionRate")), Is.EqualTo(turnSpeed).Within(.08f));
        Contacts("Player1"); Contacts("Player2");
        Keys(); yield return Wait(.6f); Assert.IsFalse(animator.GetBool("isWalking"));
    }
    [UnityTest]
    public IEnumerator SoloCarriedComputerStopsAtWallWithHandsAttached()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity"); yield return new EnterPlayMode();
        Keys(); yield return Wait(.3f);
        var player = GameObject.Find("Player1"); var item = GameObject.Find("Carry_Props_Computer_01");
        Position(player, new Vector3(30, .35f, 28), Quaternion.identity);
        Position(GameObject.Find("Player2"), new Vector3(35, .35f, 28), Quaternion.identity);
        Position(item, new Vector3(30, .75f, 28.65f), Quaternion.identity);
        Call(Actor("Player1"), "PickUpObject", C(item, "HInteractions.Liftable")); yield return Wait(1f);
        var bounds = item.GetComponent<BoxCollider>().bounds;
        var wall = Wall(new Vector3(bounds.center.x, 1f, bounds.max.z + .18f), new Vector3(4, 3, .04f));
        Assert.Less(Penetration(item, wall), .001f);
        Keys("W"); float worst = 0f;
        for (float end = Time.time + 1.6f; Time.time < end;)
        { yield return null; worst = Mathf.Max(worst, Penetration(item, wall)); }
        Debug.Log("MOTION solo object wall max penetration=" + worst);
        Assert.Less(worst, .008f); Assert.Less(Penetration(player, wall), .008f);
        Assert.IsFalse(player.GetComponent<Animator>().GetBool("isWalking"), "Blocked object stops its holder's gait");
        Contacts("Player1");
        CooperativeHoseTests.Capture("Temp/Gameloop-Solo-Wall.png", player.transform.position + Vector3.up * .4f);
        Keys("S"); Vector3 before = player.transform.position;
        for (float end = Time.time + .6f; Time.time < end;)
        { yield return null; worst = Mathf.Max(worst, Penetration(item, wall)); }
        Keys();
        Assert.Less(worst, .008f, "Sharp retreat turn must follow the held object's real pivot arc");
        Assert.Greater(Vector3.Distance(before, player.transform.position), .15f, "Can retreat from the obstacle");
        Contacts("Player1");
    }
    [UnityTest]
    public IEnumerator CooperativeTranslationRotationAndOrbitRespectWall()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity"); yield return new EnterPlayMode();
        Keys(); yield return Wait(.3f); TeamSetup(true); yield return Wait(2f);
        var table = Team().gameObject; var bounds = table.GetComponent<BoxCollider>().bounds;
        var wall = Wall(new Vector3(bounds.max.x + .22f, 1f, bounds.center.z), new Vector3(.04f, 3, 10));
        float worst = 0f; Keys("D", "RightArrow");
        for (float end = Time.time + 1.3f; Time.time < end;)
        { yield return null; worst = Mathf.Max(worst, Penetration(table, wall)); }
        Keys(); yield return Wait(.3f);
        Quaternion before = table.GetComponent<Rigidbody>().rotation;
        Keys("L");
        for (float end = Time.time + 1f; Time.time < end;)
        { yield return null; worst = Mathf.Max(worst, Penetration(table, wall)); }
        Keys(); yield return Wait(.3f);
        Debug.Log("MOTION team wall penetration=" + worst + " permitted yaw=" + Quaternion.Angle(before, table.GetComponent<Rigidbody>().rotation));
        Assert.Less(worst, .008f); Assert.Less(Quaternion.Angle(before, table.GetComponent<Rigidbody>().rotation), 5f);
        Keys("D");
        for (float end = Time.time + .7f; Time.time < end;)
        { yield return null; worst = Mathf.Max(worst, Penetration(table, wall)); }
        Keys(); yield return Wait(.4f);
        Assert.Less(worst, .008f);
        foreach (string name in new[] { "Player1", "Player2" })
        { Assert.Less(Penetration(GameObject.Find(name), wall), .008f); Contacts(name); }
        CooperativeHoseTests.Capture("Temp/Gameloop-Team-Wall.png", table.transform.position);
    }
}
