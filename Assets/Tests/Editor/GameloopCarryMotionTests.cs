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
    public IEnumerator EitherPlayerCanReleaseWhilePartnerKeepsSoloCarryAndRejoins()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity"); yield return new EnterPlayMode();
        yield return IndividualRelease();
    }

    static IEnumerator IndividualRelease()
    {
        yield return Wait(.5f);
        Wall(new Vector3(30f, -.1f, 30f), new Vector3(20f, .2f, 20f));
        foreach (string releaseName in new[] { "Player1", "Player2" })
        {
            TeamSetup(true); yield return Wait(2f);
            var team = Team();
            var leaving = Actor(releaseName);
            var staying = Actor(releaseName == "Player1" ? "Player2" : "Player1");
            string role = P(staying, "CurrentCarryRole").ToString();
            Keys(releaseName == "Player1" ? "F" : "RightCtrl"); yield return Wait(.1f); Keys();
            Assert.IsNull(P(leaving, "HeldObject"));
            Assert.AreSame(team, P(staying, "HeldObject"));
            Assert.IsFalse((bool)P(staying, "IsPickingUp"));
            Assert.IsFalse((bool)P(staying, "IsPlacingDown"));
            Assert.AreEqual(role, P(staying, "CurrentCarryRole").ToString());
            Assert.That((float)P(team, "LoadPerPlayerKg"), Is.EqualTo(team.GetComponent<Rigidbody>().mass).Within(.001f));
            Position(leaving.gameObject, new Vector3(35f, .35f, 30f), Quaternion.identity);
            var freeStart = leaving.transform.position;
            Keys(releaseName == "Player1" ? "W" : "UpArrow"); yield return Wait(.4f); Keys();
            Assert.Greater(Vector3.Distance(freeStart, leaving.transform.position), .1f, "Released player remains free to walk");
            yield return Wait(1f);
            Contacts(staying.name);
            Vector3 start = team.transform.position;
            Keys(staying.name == "Player1" ? "A" : "LeftArrow"); yield return Wait(1f); Keys();
            Assert.Greater(Vector3.Distance(start, team.transform.position), .01f, "Remaining holder must retain solo movement");
            Contacts(staying.name);
            var carryRole = Enum.Parse(T("TA.AdaptiveAnimation.CarryRole"), role == "Front" ? "Back" : "Front");
            Position(leaving.gameObject, (Vector3)Call(team, "GetStance", carryRole, .35f), role == "Front" ? Quaternion.identity : Quaternion.Euler(0,180,0));
            Call(leaving, "PickUpObject", team); yield return Wait(2f);
            Assert.AreEqual("Carrying", P(team, "State").ToString());
            Call(Actor("Player1"), "ReleaseHeldObject"); Call(Actor("Player2"), "ReleaseHeldObject");
            Assert.AreEqual("Idle", P(team, "State").ToString());
        }
    }

    [UnityTest]
    public IEnumerator FurnishedRoomTableCanBeCarriedWithNearbyLooseProps()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity"); yield return new EnterPlayMode();
        yield return FurnishedTable("Carry_Props_Table_01");
    }

    [UnityTest]
    public IEnumerator FurnishedOfficeDeskCanBeCarriedWithNearbyLooseProps()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity"); yield return new EnterPlayMode();
        yield return FurnishedTable("Carry_Props_Table_05");
    }

    [UnityTest]
    public IEnumerator StaggeredTablePickupRestoresSupportedLoadCollisions()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity"); yield return new EnterPlayMode();
        yield return FurnishedTable("Carry_Props_Table_01", true);
    }

    static Rigidbody[] Loads(Component team) => ((System.Collections.IEnumerable)team.GetType().GetField("supportedLoads", Flags).GetValue(team)).Cast<Rigidbody>().ToArray();
    static Collider[] Solids(Rigidbody body) => body.GetComponentsInChildren<Collider>()
        .Where(c => c.enabled && !c.isTrigger && c.attachedRigidbody == body).ToArray();

    static IEnumerator FurnishedTable(string tableName, bool staggered = false)
    {
        Keys(); yield return Wait(.5f);
        var table = GameObject.Find(tableName); var team = C(table, "TA.AdaptiveAnimation.CooperativeCarryObject");
        var role = T("TA.AdaptiveAnimation.CarryRole");
        var p1 = GameObject.Find("Player1"); var p2 = GameObject.Find("Player2");
        Vector3 initial = table.GetComponent<Rigidbody>().position;
        // Keep the authored table, computers, chairs and room geometry in place.
        Position(p1, (Vector3)Call(team, "GetStance", Enum.Parse(role, "Front"), .35f), table.transform.rotation * Quaternion.Euler(0, 180f, 0));
        Position(p2, (Vector3)Call(team, "GetStance", Enum.Parse(role, "Back"), .35f), table.transform.rotation);
        yield return Wait(.4f);
        Call(Actor("Player1"), "BeginPickup", team);
        if (staggered)
        {
            for (float deadline = Time.time + 5f; Time.time < deadline &&
                (!ReferenceEquals(P(Actor("Player1"), "HeldObject"), team) || (bool)P(Actor("Player1"), "IsPickingUp"));) yield return null;
            Assert.AreSame(team, P(Actor("Player1"), "HeldObject"));
            Assert.AreEqual("WaitingForPartner", P(team, "State").ToString());
            yield return Wait(.7f);
        }
        Call(Actor("Player2"), "BeginPickup", team);
        for (float deadline = Time.time + 8f; Time.time < deadline &&
            (!ReferenceEquals(P(Actor("Player1"), "HeldObject"), team) || !ReferenceEquals(P(Actor("Player2"), "HeldObject"), team) ||
             P(team, "State").ToString() != "Carrying");) yield return null;
        Assert.AreSame(team, P(Actor("Player1"), "HeldObject"));
        Assert.AreSame(team, P(Actor("Player2"), "HeldObject"));
        Assert.AreEqual("Carrying", P(team, "State").ToString(), RoomState(team));
        yield return Wait(.6f); // Let the existing pickup-to-hold IK blend finish.
        CaptureTable("Temp/Gameloop-Furnished-Before-" + tableName + ".png", table.transform.position);
        Contacts("Player1"); Contacts("Player2");
        var before = table.GetComponent<Rigidbody>().position;
        var player1Before = p1.GetComponent<Rigidbody>().position;
        var player2Before = p2.GetComponent<Rigidbody>().position;
        Keys("W", "UpArrow");
        for (float until = Time.time + 1f; Time.time < until;)
        { yield return Wait(.15f); Contacts("Player1"); Contacts("Player2"); }
        Keys(); yield return Wait(.25f);
        float travel = Vector3.ProjectOnPlane(table.GetComponent<Rigidbody>().position - before, Vector3.up).magnitude;
        Debug.Log("FURNISHED TABLE " + tableName + " displacement=" + travel + " start=" + initial + " carry=" + before);
        Assert.Greater(travel, .15f, "Both real keyboard inputs must move the table in its furnished room. " + RoomState(team));
        Assert.Greater(Vector3.ProjectOnPlane(p1.GetComponent<Rigidbody>().position - player1Before, Vector3.up).magnitude, .15f);
        Assert.Greater(Vector3.ProjectOnPlane(p2.GetComponent<Rigidbody>().position - player2Before, Vector3.up).magnitude, .15f);
        Assert.AreEqual("Carrying", P(team, "State").ToString());
        Contacts("Player1"); Contacts("Player2");
        CaptureTable("Temp/Gameloop-Furnished-" + tableName + ".png", table.transform.position);
        CaptureTable("Temp/Gameloop-Furnished-Side-" + tableName + ".png", table.transform.position, true);
        if (staggered)
        {
            var loads = Loads(team);
            Assert.Greater(loads.Length, 0, "The real desktop loads must remain registered");
            foreach (var load in loads)
            foreach (var solid in Solids(load))
            {
                Assert.IsFalse(load.isKinematic);
                Assert.IsTrue(Physics.GetIgnoreCollision(solid, p1.GetComponent<CapsuleCollider>()));
                Assert.IsTrue(Physics.GetIgnoreCollision(solid, p2.GetComponent<CapsuleCollider>()));
                foreach (var surface in Solids(table.GetComponent<Rigidbody>()))
                    Assert.IsFalse(Physics.GetIgnoreCollision(solid, surface), "Cargo still collides with the table");
            }
            Position(loads[0].gameObject, new Vector3(30, 3, 30), Quaternion.identity);
            yield return Wait(.12f);
            Assert.IsFalse(Loads(team).Contains(loads[0]), "A load leaving the tabletop must be released");
            foreach (var solid in Solids(loads[0]))
            {
                Assert.IsFalse(Physics.GetIgnoreCollision(solid, p1.GetComponent<CapsuleCollider>()));
                Assert.IsFalse(Physics.GetIgnoreCollision(solid, p2.GetComponent<CapsuleCollider>()));
            }
            Call(Actor("Player1"), "ReleaseHeldObject"); yield return Wait(.12f);
            Assert.IsNull(P(Actor("Player1"), "HeldObject"));
            Assert.AreSame(team, P(Actor("Player2"), "HeldObject"));
            foreach (var load in Loads(team))
            foreach (var solid in Solids(load))
            {
                Assert.IsFalse(Physics.GetIgnoreCollision(solid, p1.GetComponent<CapsuleCollider>()));
                Assert.IsTrue(Physics.GetIgnoreCollision(solid, p2.GetComponent<CapsuleCollider>()));
            }
            Call(Actor("Player2"), "ReleaseHeldObject"); yield return Wait(.12f);
            Assert.IsEmpty(Loads(team));
            Assert.IsNull(P(Actor("Player1"), "HeldObject")); Assert.IsNull(P(Actor("Player2"), "HeldObject"));
            foreach (var load in loads)
            foreach (var solid in Solids(load))
            {
                Assert.IsFalse(Physics.GetIgnoreCollision(solid, p1.GetComponent<CapsuleCollider>()));
                Assert.IsFalse(Physics.GetIgnoreCollision(solid, p2.GetComponent<CapsuleCollider>()));
            }
        }
    }

    static void CaptureTable(string path, Vector3 point, bool side = false)
    {
        var go = new GameObject("TableTestCamera"); var camera = go.AddComponent<Camera>();
        go.transform.position = point + (side ? new Vector3(-3f, 1.5f, .1f) : new Vector3(.2f, 5f, -.5f));
        go.transform.LookAt(point);
        camera.orthographic = true; camera.orthographicSize = 2.5f;
        var rt = new RenderTexture(960, 960, 24); var old = RenderTexture.active;
        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
        var texture = new Texture2D(960, 960, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 960, 960), 0, 0); texture.Apply();
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        RenderTexture.active = old; camera.targetTexture = null;
        UnityEngine.Object.Destroy(go); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(texture);
    }

    static string RoomState(Component team)
    {
        var text = new System.Text.StringBuilder("Furnished room: table=" + team.name + " state=" + P(team, "State"));
        foreach (var name in new[] { "Player1", "Player2" })
        {
            var actor = Actor(name); var body = actor.GetComponent<Rigidbody>();
            var stance = (Vector3)Call(team, "GetStance", Call(team, "GetRole", actor), body.position.y);
            var grip = actor.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectGripController"));
            text.Append("; " + name + " stance error=" + Vector3.Distance(body.position, stance));
            foreach (var field in new[] { "status", "leftDistance", "rightDistance", "leftMaximumReach", "rightMaximumReach" })
                text.Append(" " + field + "=" + grip.GetType().GetField(field, Flags).GetValue(grip));
        }
        return text.ToString();
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
