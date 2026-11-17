using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public class GameloopPlacementTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static Component C(GameObject go, string type) => go.GetComponent(T(type));
    static object P(object o, string name) => o.GetType().GetProperty(name).GetValue(o);
    static object F(object o, string name) => o.GetType().GetField(name, Flags).GetValue(o);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethods(Flags)
        .First(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v)).Invoke(o, args);
    static Component Actor(string name) => C(GameObject.Find(name), "HPlayer.InteractionController");
    static IEnumerator Wait(float seconds) { for (float end = Time.time + seconds; Time.time < end;) yield return null; }
    static void Position(GameObject go, Vector3 position, Quaternion rotation)
    {
        go.transform.SetPositionAndRotation(position, rotation);
        var body = go.GetComponent<Rigidbody>(); body.position = position; body.rotation = rotation;
        if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        Physics.SyncTransforms();
    }
    static BoxCollider Box(string name, Vector3 position, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.position = position; go.transform.localScale = size; Physics.SyncTransforms();
        return go.GetComponent<BoxCollider>();
    }
    static Collider[] SolidShapes(GameObject item)
    {
        var body = item.GetComponent<Rigidbody>();
        return item.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger && c.attachedRigidbody == body).ToArray();
    }
    static Bounds SolidBounds(GameObject item)
    {
        var shapes = SolidShapes(item); Assert.IsNotEmpty(shapes);
        Bounds bounds = shapes[0].bounds;
        foreach (var shape in shapes) bounds.Encapsulate(shape.bounds);
        return bounds;
    }
    static float Penetration(GameObject item, Collider obstacle)
    {
        float worst = 0f;
        foreach (var shape in SolidShapes(item))
            if (Physics.ComputePenetration(shape, shape.transform.position, shape.transform.rotation,
                obstacle, obstacle.transform.position, obstacle.transform.rotation, out _, out float depth)) worst = Mathf.Max(worst, depth);
        return worst;
    }
    static void Unlocked(Component actor)
    {
        Assert.IsFalse((bool)P(actor, "IsPickingUp"));
        Assert.IsFalse((bool)P(actor, "IsPlacingDown"));
        Assert.IsFalse((bool)F(C(actor.gameObject, "ThirdPersonPlayerController"), "_movementLocked"));
    }
    static GameObject Computer(float height = .35f)
    {
        Position(GameObject.Find("Player1"), new Vector3(30f, height, 28f), Quaternion.identity);
        Position(GameObject.Find("Player2"), new Vector3(35f, height, 28f), Quaternion.identity);
        var computer = GameObject.Find("Carry_Props_Computer_01");
        Position(computer, new Vector3(30f, height + .4f, 28.65f), Quaternion.identity);
        Call(Actor("Player1"), "PickUpObject", C(computer, "HInteractions.Liftable"));
        return computer;
    }
    static GameObject Table()
    {
        var table = GameObject.Find("Carry_Props_Table_01");
        var team = C(table, "TA.AdaptiveAnimation.CooperativeCarryObject");
        Position(table, new Vector3(30f, .75f, 30f), Quaternion.identity);
        var role = T("TA.AdaptiveAnimation.CarryRole");
        Position(GameObject.Find("Player1"), (Vector3)Call(team, "GetStance", Enum.Parse(role, "Front"), .35f), Quaternion.Euler(0f, 180f, 0f));
        Position(GameObject.Find("Player2"), (Vector3)Call(team, "GetStance", Enum.Parse(role, "Back"), .35f), Quaternion.identity);
        Call(Actor("Player1"), "PickUpObject", team); Call(Actor("Player2"), "PickUpObject", team);
        return table;
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

    static IEnumerator VerifyPlacement(GameObject item, Component first, Component second, Collider floor, Collider wall)
    {
        var liftable = C(item, "HInteractions.Liftable");
        var animator = first.GetComponent<Animator>();
        float initialHeight = item.transform.position.y;
        Call(first, "BeginPlaceDown");
        Assert.IsTrue((bool)P(first, "IsPlacingDown"), "A valid nearby ground pose must start placement");
        if (second) Assert.IsTrue((bool)P(second, "IsPlacingDown"), "Furniture placement must coordinate both operators");
        bool sawOwnedLowering = false, releasedAtContact = false;
        float wallDepth = 0f, floorDepth = 0f;
        for (float deadline = Time.time + 5f; P(first, "HeldObject") != null && Time.time < deadline;)
        {
            yield return null;
            wallDepth = Mathf.Max(wallDepth, Penetration(item, wall));
            floorDepth = Mathf.Max(floorDepth, Penetration(item, floor));
            var state = animator.GetCurrentAnimatorStateInfo(0);
            float contact = (float)F(first, "placementContactTime");
            if (P(first, "HeldObject") != null && state.IsName("Pickup") &&
                state.normalizedTime < contact * .95f && item.transform.position.y < initialHeight - .02f)
            {
                Assert.AreSame(liftable, P(first, "HeldObject"));
                if (second) Assert.AreSame(liftable, P(second, "HeldObject"));
                sawOwnedLowering = true;
            }
            if (P(first, "HeldObject") == null)
                releasedAtContact = state.IsName("Pickup") && state.normalizedTime >= contact - .08f;
        }
        Assert.IsTrue(sawOwnedLowering, "Object remains owned during visible lowering before the animation contact");
        Assert.IsTrue(releasedAtContact, "Actual pickup clip contact event commits placement");
        Assert.IsNull(P(first, "HeldObject"));
        if (second) Assert.IsNull(P(second, "HeldObject"));
        for (float end = Time.time + 1.5f; Time.time < end;)
        {
            yield return null;
            wallDepth = Mathf.Max(wallDepth, Penetration(item, wall));
            floorDepth = Mathf.Max(floorDepth, Penetration(item, floor));
        }
        Debug.Log("PLACEMENT " + item.name + " wall=" + wallDepth + " floor=" + floorDepth + " bottom=" + SolidBounds(item).min.y);
        Assert.Less(wallDepth, .008f, "Placement and subsequent settling must not penetrate the wall");
        Assert.Less(floorDepth, .008f, "Placement and subsequent settling must not penetrate the floor");
        Assert.That(SolidBounds(item).min.y, Is.InRange(-.008f, .04f), "Object must settle on the floor rather than hover");
        Assert.IsFalse(item.GetComponent<Rigidbody>().isKinematic, "Released level prop restores dynamic physics");
        Assert.Less(new Vector2(item.GetComponent<Rigidbody>().velocity.x, item.GetComponent<Rigidbody>().velocity.z).magnitude, .15f, "Placement must not eject/throw the object");
        Unlocked(first); if (second) Unlocked(second);
    }

    [UnityTest]
    public IEnumerator ComputerPlacementNearWallStaysAboveFloor()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity"); yield return new EnterPlayMode();
        yield return Wait(.3f);
        var floor = Box("PlacementTestFloor", new Vector3(30f, -.1f, 30f), new Vector3(20f, .2f, 20f));
        var computer = Computer(); yield return Wait(1f);
        var bounds = SolidBounds(computer);
        var wall = Box("PlacementTestWall", new Vector3(bounds.center.x, 1f, bounds.max.z + .03f), new Vector3(4f, 3f, .04f));
        Assert.Less(Penetration(computer, wall), .001f, "Initial carried pose is clear of the wall");
        yield return VerifyPlacement(computer, Actor("Player1"), null, floor, wall);
        CooperativeHoseTests.Capture("Temp/Gameloop-Computer-Placed.png", computer.transform.position + Vector3.up * .2f);
    }

    [UnityTest]
    public IEnumerator CooperativeTablePlacementNearWallReleasesBothSafely()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity"); yield return new EnterPlayMode();
        yield return Wait(.3f);
        var floor = Box("PlacementTestFloor", new Vector3(30f, -.1f, 30f), new Vector3(20f, .2f, 20f));
        var table = Table(); yield return Wait(2f);
        Assert.AreEqual("Carrying", P(C(table, "TA.AdaptiveAnimation.CooperativeCarryObject"), "State").ToString());
        var bounds = SolidBounds(table);
        var wall = Box("PlacementTestWall", new Vector3(bounds.max.x + .03f, 1f, bounds.center.z), new Vector3(.04f, 3f, 8f));
        Assert.Less(Penetration(table, wall), .001f);
        yield return VerifyPlacement(table, Actor("Player1"), Actor("Player2"), floor, wall);
    }

    [UnityTest]
    public IEnumerator PlacementWithoutGroundKeepsOwnershipAndRecoversControls()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity"); yield return new EnterPlayMode();
        yield return Wait(.3f);
        var computer = Computer(20.35f); yield return Wait(1f);
        var actor = Actor("Player1"); var held = P(actor, "HeldObject");
        Call(actor, "BeginPlaceDown");
        yield return Wait(4f);
        Assert.AreSame(held, P(actor, "HeldObject"), "No support within placement range must not release an object into empty space");
        Assert.IsTrue(computer.GetComponent<Rigidbody>().isKinematic);
        Unlocked(actor);
    }

    [UnityTest]
    public IEnumerator ObstacleEnteringPlacementPathCancelsWithoutPenetration()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity"); yield return new EnterPlayMode();
        yield return Wait(.3f);
        var floor = Box("PlacementTestFloor", new Vector3(30f, -.1f, 30f), new Vector3(20f, .2f, 20f));
        var computer = Computer(); yield return Wait(1f);
        var actor = Actor("Player1"); var held = P(actor, "HeldObject");
        Call(actor, "BeginPlaceDown");
        Assert.IsTrue((bool)P(actor, "IsPlacingDown"));
        var bounds = SolidBounds(computer);
        var blocker = Box("LatePlacementBlocker", new Vector3(bounds.center.x, bounds.min.y - .12f, bounds.center.z), new Vector3(2f, .04f, 2f));
        Assert.Less(Penetration(computer, blocker), .001f);
        float worstBlock = 0f, worstFloor = 0f;
        for (float end = Time.time + 4f; Time.time < end;)
        {
            yield return null;
            worstBlock = Mathf.Max(worstBlock, Penetration(computer, blocker));
            worstFloor = Mathf.Max(worstFloor, Penetration(computer, floor));
        }
        Assert.Less(worstBlock, .008f, "New obstacles must not be crossed by the lowering animation or final commit");
        Assert.Less(worstFloor, .008f);
        Assert.AreSame(held, P(actor, "HeldObject"), "Blocked placement retains ownership instead of forcing an unsafe release");
        Unlocked(actor);
    }

    [UnityTest]
    public IEnumerator BlockedTablePlacementRestoresBothOperatorsCarryPose()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity"); yield return new EnterPlayMode();
        yield return Wait(.3f);
        var floor = Box("PlacementTestFloor", new Vector3(30f, -.1f, 30f), new Vector3(20f, .2f, 20f));
        var table = Table(); yield return Wait(2f);
        var first = Actor("Player1"); var second = Actor("Player2");
        var team = C(table, "TA.AdaptiveAnimation.CooperativeCarryObject");
        Assert.AreEqual("Carrying", P(team, "State").ToString());
        var held = P(first, "HeldObject");
        float carryHeight = table.GetComponent<Rigidbody>().position.y;
        Call(first, "BeginPlaceDown");
        Assert.IsTrue((bool)P(first, "IsPlacingDown"));
        Assert.IsTrue((bool)P(second, "IsPlacingDown"));
        var bounds = SolidBounds(table);
        var blocker = Box("LateTablePlacementBlocker", new Vector3(bounds.center.x, bounds.min.y - .12f, bounds.center.z),
            new Vector3(bounds.size.x + .4f, .04f, bounds.size.z + .4f));
        Assert.Less(Penetration(table, blocker), .001f, "Blocker starts below the carried table, not overlapping it");
        float worstBlock = 0f, worstFloor = 0f;
        for (float end = Time.time + 4f; Time.time < end;)
        {
            yield return null;
            worstBlock = Mathf.Max(worstBlock, Penetration(table, blocker));
            worstFloor = Mathf.Max(worstFloor, Penetration(table, floor));
        }
        Debug.Log("PLACEMENT cancelled table blocker=" + worstBlock + " floor=" + worstFloor +
            " recoveredHeight=" + table.GetComponent<Rigidbody>().position.y + " expectedHeight=" + carryHeight);
        Assert.Less(worstBlock, .008f); Assert.Less(worstFloor, .008f);
        Assert.AreSame(held, P(first, "HeldObject")); Assert.AreSame(held, P(second, "HeldObject"));
        Unlocked(first); Unlocked(second);
        Assert.AreEqual("Carrying", P(team, "State").ToString(), "Cancelled placement must resume the cooperative carry state");
        Assert.That(table.GetComponent<Rigidbody>().position.y, Is.EqualTo(carryHeight).Within(.03f),
            "Retaining ownership must also restore hand-height carry, not leave the table lowered");
        foreach (var actor in new[] { first, second })
            typeof(GameloopMigrationTests).GetMethod("VisiblePropContacts", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { actor.gameObject });
    }
}
