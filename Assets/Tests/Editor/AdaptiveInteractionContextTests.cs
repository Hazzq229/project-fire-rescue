using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// These tests enter real Play Mode while staying in the existing editor test
// assembly. Reflection keeps that assembly independent of Assembly-CSharp.
public class AdaptiveInteractionContextTests
{
    const string Scene = "Assets/Scenes/Gameloop_Demo.unity";
    const string Prefix = "TA.AdaptiveAnimation.";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static Component C(GameObject go, string name) => go.GetComponent(T(name));
    static object P(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
    static object F(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethods(Flags)
        .First(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v)).Invoke(value, args);
    static object HoseCheck(string name, params object[] args) => typeof(CooperativeHoseTests)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    static Component Actor(GameObject player) => C(player, "HPlayer.InteractionController");
    static Component Manager(GameObject player)
    {
        var manager = C(player, Prefix + "AdaptiveAnimationManager");
        Assert.NotNull(manager, player.name + " must receive the opted-in gameplay context manager");
        return manager;
    }
    static object Context(Component manager) => P(manager, "Context");
    static object Result(Component manager) => P(manager, "Result");
    static void Expect(object value, string property, string expected) => Assert.AreEqual(expected, P(value, property).ToString(), property);
    static IEnumerator Wait(float seconds) { for (float until = Time.time + seconds; Time.time < until;) yield return null; }
    static void Position(GameObject go, Vector3 position, Quaternion rotation)
    {
        go.transform.SetPositionAndRotation(position, rotation);
        var body = go.GetComponent<Rigidbody>(); body.position = position; body.rotation = rotation;
        if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        Physics.SyncTransforms();
    }
    static void Empty(Component manager)
    {
        var context = Context(manager);
        Expect(context, "InteractionType", "None"); Expect(context, "Phase", "None"); Expect(context, "Role", "None");
        Assert.IsNull(P(context, "Source")); Assert.IsNull(P(context, "HeldObject")); Assert.IsNull(P(context, "PendingObject"));
        Assert.IsNull(P(context, "ObjectTarget"));
        Assert.AreEqual(0f, (float)P(context, "ObjectWeightKg"));
        Assert.AreEqual(0f, (float)P(context, "EffectiveWeightKg"));
        Assert.IsNull(P(Result(manager), "GripProvider"));
        Assert.IsTrue(string.IsNullOrEmpty((string)P(Result(manager), "BaseAnimationState")));
    }
    static GameObject TestFloor()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "ContextTestFloor";
        floor.transform.position = new Vector3(30f, -.1f, 30f);
        floor.transform.localScale = new Vector3(20f, .2f, 20f);
        Physics.SyncTransforms();
        return floor;
    }
    static IEnumerator Ready()
    {
        HoseCheck("ResetKeyboard");
        var adapter = C(GameObject.Find("FR_Level_1"), Prefix + "AdaptiveSceneProps");
        for (float until = Time.time + 3f; !(bool)P(adapter, "IsReady") && Time.time < until;) yield return null;
        Assert.IsTrue((bool)P(adapter, "IsReady"));
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
    public IEnumerator RepeatedReadsReuseResultsAndBindingsWithoutDelayingLiveChanges()
    {
        EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return Ready();
        TestFloor();
        var player = GameObject.Find("Player1"); var actor = Actor(player); var manager = Manager(player);
        var computer = GameObject.Find("Carry_Props_Computer_01");
        var item = C(computer, "HInteractions.Liftable");
        var load = C(computer, Prefix + "CarryAnimationLoad");
        var provider = C(computer, Prefix + "ScenePropGripProvider");
        Position(player, new Vector3(30f, .35f, 28f), Quaternion.identity);
        Position(GameObject.Find("Player2"), new Vector3(35f, .35f, 28f), Quaternion.identity);
        Position(computer, new Vector3(30f, .75f, 28.65f), Quaternion.identity);
        yield return Wait(.3f);
        Call(actor, "BeginPickup", item);
        for (float until = Time.time + 4f; P(actor, "HeldObject") == null && Time.time < until;) yield return null;
        Assert.AreSame(item, P(actor, "HeldObject"));
        var pickupSnapshot = Context(manager);
        Assert.IsTrue((bool)P(pickupSnapshot, "IsPickingUp"), "Capture between grab and pickup completion");
        Assert.AreEqual("Base Layer.Pickup", P(Result(manager), "AnimationStateHint"));
        Call(actor, "NotifyPickupExited");
        Assert.AreEqual("Carry Layer.CarryHold", P(Result(manager), "AnimationStateHint"), "Completion is visible without a yield");
        var processor = T(Prefix + "ProceduralLayerProcessor").GetMethod("Process");
        Assert.AreEqual("Base Layer.Pickup", P(processor.Invoke(null, new[] { pickupSnapshot }), "AnimationStateHint"),
            "Routing a saved snapshot must not consult the actor's changed pickup flag");
        int frame = Time.frameCount;
        int revision = (int)P(manager, "ContextRevision"), bindings = (int)P(manager, "BindingRefreshCount");
        for (int i = 0; i < 100; i++) { Context(manager); Result(manager); Call(manager, "Update"); }
        Assert.AreEqual(revision, P(manager, "ContextRevision"), "Unchanged readers/Update share one published result");
        Assert.AreEqual(bindings, P(manager, "BindingRefreshCount"), "Unchanged reads do not repeat component discovery");
        Call(load, "Configure", 41f);
        Assert.AreEqual(41f, P(Context(manager), "ObjectWeightKg"));
        Assert.AreEqual(revision + 1, P(manager, "ContextRevision"));
        var shape = computer.GetComponent<BoxCollider>(); var size = shape.size;
        shape.size += Vector3.right * .03f;
        Assert.That(((Vector3)P(Context(manager), "ObjectSize")).x,
            Is.EqualTo((size.x + .03f) * Mathf.Abs(shape.transform.lossyScale.x)).Within(.00001f));
        computer.transform.localScale = new Vector3(1.1f, 1f, 1f);
        Assert.That(((Vector3)P(Context(manager), "ObjectSize")).x, Is.EqualTo((size.x + .03f) * 1.1f).Within(.00001f));
        ((Behaviour)provider).enabled = false;
        Assert.IsNull(P(Result(manager), "GripProvider"));
        ((Behaviour)provider).enabled = true;
        Assert.AreSame(provider, P(Result(manager), "GripProvider"));
        ((Behaviour)load).enabled = false;
        Assert.IsFalse((bool)P(Result(manager), "HasLoad"));
        ((Behaviour)load).enabled = true;
        Assert.AreEqual(41f, P(Result(manager), "EffectiveWeightKg"));
        Assert.AreEqual(bindings, P(manager, "BindingRefreshCount"));
        Assert.AreEqual(frame, Time.frameCount, "All invalidation assertions happen in the same frame");
        revision = (int)P(manager, "ContextRevision");
        Call(manager, "RefreshContext");
        Assert.AreEqual(bindings + 1, P(manager, "BindingRefreshCount"));
        Assert.AreEqual(revision, P(manager, "ContextRevision"), "Forced rebind need not republish identical data");
        yield return null;
        Context(manager);
        Assert.Greater((int)P(manager, "BindingRefreshCount"), bindings + 1, "Next frame checks structural changes");
        Debug.Log("CONTEXT CACHE: 100 Context + 100 Result + 100 Update reads reused result/bindings; same-frame load, size, scale, provider and pickup changes refreshed immediately.");
    }

    [UnityTest]
    public IEnumerator ComputerContextTracksPickupContactLoadAndLegacyFallback()
    {
        EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return Ready();
        TestFloor();
        var player = GameObject.Find("Player1"); var second = GameObject.Find("Player2");
        var manager = Manager(player); var other = Manager(second); var actor = Actor(player);
        Empty(manager); Empty(other);
        var computer = GameObject.Find("Carry_Props_Computer_01");
        var item = C(computer, "HInteractions.Liftable");
        var provider = C(computer, Prefix + "ScenePropGripProvider");
        var load = C(computer, Prefix + "CarryAnimationLoad");
        var dimensions = (Vector3)P(C(computer, Prefix + "AdaptiveSceneProp"), "Dimensions");
        float physicalMass = computer.GetComponent<Rigidbody>().mass;
        Position(player, new Vector3(30f, .35f, 28f), Quaternion.identity);
        Position(second, new Vector3(35f, .35f, 28f), Quaternion.identity);
        Position(computer, new Vector3(30f, .75f, 28.65f), Quaternion.identity);
        yield return Wait(.3f);
        Call(actor, "BeginPickup", item);
        Assert.IsTrue((bool)P(actor, "IsPickingUp"));
        var pending = Context(manager);
        Expect(pending, "InteractionType", "CarryObject"); Expect(pending, "ObjectType", "Box"); Expect(pending, "Phase", "Preparing");
        Assert.AreSame(actor, P(pending, "Actor")); Assert.AreSame(item, P(pending, "PendingObject"));
        Assert.IsNull(P(pending, "HeldObject"));
        Assert.IsNull(P(Result(manager), "GripProvider"), "Pending pickup must never enable holding IK before contact");
        Assert.AreEqual("Base Layer.Pickup", P(Result(manager), "BaseAnimationState"));
        for (float until = Time.time + 4f; (bool)P(actor, "IsPickingUp") && Time.time < until;) yield return null;
        Assert.AreSame(item, P(actor, "HeldObject"), "Actual Pickup_Grab must attach the computer");
        yield return Wait(.8f);
        var held = Context(manager);
        Expect(held, "Phase", "Carrying"); Expect(held, "Role", "None");
        Assert.AreSame(item, P(held, "HeldObject")); Assert.IsNull(P(held, "PendingObject"));
        Assert.AreSame(provider, P(Result(manager), "GripProvider"));
        Assert.AreEqual("Carry Layer.CarryHold", P(Result(manager), "BaseAnimationState"));
        Assert.Less(Vector3.Distance(dimensions, (Vector3)P(held, "ObjectSize")), .001f,
            "Object size must use the normalized prop's authored dimensions");
        var shape = computer.GetComponent<BoxCollider>();
        Vector3 originalSize = shape.size;
        shape.size += Vector3.right * .02f;
        Assert.That(((Vector3)P(Context(manager), "ObjectSize")).x,
            Is.EqualTo(dimensions.x + .02f * Mathf.Abs(shape.transform.lossyScale.x)).Within(.001f),
            "A changed live size parameter must not require releasing and re-picking the object");
        shape.size = originalSize;
        Empty(other);
        var lean = player.GetComponentInChildren(T(Prefix + "HeldObjectBodyLeanController"));
        Call(load, "Configure", 50f);
        Assert.That((float)P(Context(manager), "ObjectWeightKg"), Is.EqualTo(50f).Within(.001f));
        Assert.That((float)P(Result(manager), "EffectiveWeightKg"), Is.EqualTo(50f).Within(.001f));
        yield return Wait(1.5f);
        Assert.That((float)F(lean, "currentLoadKg"), Is.EqualTo(50f).Within(.01f));
        Assert.That((float)F(lean, "currentStylizedAngle"), Is.EqualTo(24f).Within(.6f));
        HoseCheck("CheckMovingContacts", player);
        ((Behaviour)load).enabled = false;
        Assert.IsFalse((bool)P(Context(manager), "HasWeight"));
        Assert.IsFalse((bool)P(Result(manager), "HasLoad"));
        Assert.AreEqual(0f, (float)P(Result(manager), "EffectiveWeightKg"));
        ((Behaviour)load).enabled = true;
        Assert.That((float)P(Result(manager), "EffectiveWeightKg"), Is.EqualTo(50f).Within(.001f));
        ((Behaviour)provider).enabled = false;
        Assert.IsNull(P(Result(manager), "GripProvider"), "A disabled provider cannot supply an active grip result");
        ((Behaviour)provider).enabled = true;
        Assert.AreSame(provider, P(Result(manager), "GripProvider"));
        ((Behaviour)manager).enabled = false;
        Empty(manager);
        yield return Wait(.8f);
        Assert.AreSame(item, P(actor, "HeldObject"), "Disabling the context layer must preserve existing ownership");
        Assert.That((float)F(lean, "currentLoadKg"), Is.EqualTo(50f).Within(.01f));
        HoseCheck("CheckMovingContacts", player);
        Assert.That((float)F(lean, "currentStylizedAngle"), Is.EqualTo(24f).Within(.6f),
            "The legacy path must keep the same calibrated load pose");
        var animator = player.GetComponent<Animator>();
        Assert.That(animator.GetLayerWeight(animator.GetLayerIndex("Carry Layer")), Is.EqualTo(1f).Within(.01f));
        ((Behaviour)manager).enabled = true;
        Assert.AreSame(item, P(Context(manager), "HeldObject"));
        Assert.That(computer.GetComponent<Rigidbody>().mass, Is.EqualTo(physicalMass).Within(.001f),
            "Animation context must not overwrite physics mass");
        ((Behaviour)item).enabled = false;
        Empty(manager);
        ((Behaviour)item).enabled = true;
        Assert.AreSame(item, P(Context(manager), "HeldObject"));
        Call(actor, "ReleaseHeldObject");
        Empty(manager); Empty(other);
        yield return Wait(.8f);
        Assert.Less(Mathf.Abs((float)F(lean, "currentStylizedAngle")), .1f);
    }

    [UnityTest]
    public IEnumerator TableContextSplitsLoadByOwnershipAndClearsAfterPlacement()
    {
        EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return Ready();
        TestFloor();
        var p1 = GameObject.Find("Player1"); var p2 = GameObject.Find("Player2");
        var a = Actor(p1); var b = Actor(p2); var m1 = Manager(p1); var m2 = Manager(p2);
        var table = GameObject.Find("Carry_Props_Table_01"); var team = C(table, Prefix + "CooperativeCarryObject");
        var load = C(table, Prefix + "CarryAnimationLoad"); Call(load, "Configure", 60f);
        Position(table, new Vector3(30f, .75f, 30f), Quaternion.identity);
        var roleType = T(Prefix + "CarryRole");
        // Reverse player numbers deliberately: role comes from station ownership.
        Position(p2, (Vector3)Call(team, "GetStance", Enum.Parse(roleType, "Front"), .35f), Quaternion.Euler(0f, 180f, 0f));
        Position(p1, (Vector3)Call(team, "GetStance", Enum.Parse(roleType, "Back"), .35f), Quaternion.identity);
        Call(b, "PickUpObject", team);
        Assert.AreSame(team, P(b, "HeldObject"));
        Expect(Context(m2), "Role", "Front");
        Assert.That((float)P(Context(m2), "ObjectWeightKg"), Is.EqualTo(60f).Within(.001f));
        Assert.That((float)P(Context(m2), "EffectiveWeightKg"), Is.EqualTo(60f).Within(.001f));
        Empty(m1);
        Call(a, "PickUpObject", team);
        for (float until = Time.time + 4f; P(team, "State").ToString() != "Carrying" && Time.time < until;) yield return null;
        Assert.AreEqual("Carrying", P(team, "State").ToString());
        yield return Wait(1.5f);
        Expect(Context(m1), "Role", "Back"); Expect(Context(m2), "Role", "Front");
        foreach (var manager in new[] { m1, m2 })
        {
            Expect(Context(manager), "InteractionType", "CarryObject"); Expect(Context(manager), "ObjectType", "Box");
            Assert.AreSame(team, P(Context(manager), "HeldObject"));
            Assert.That((float)P(Result(manager), "EffectiveWeightKg"), Is.EqualTo(30f).Within(.001f));
            HoseCheck("CheckMovingContacts", manager.gameObject);
        }
        Call(a, "BeginPlaceDown");
        Assert.IsNull(P(a, "HeldObject")); Empty(m1);
        Assert.AreSame(team, P(b, "HeldObject"));
        Assert.That((float)P(Result(m2), "EffectiveWeightKg"), Is.EqualTo(60f).Within(.001f));
        Call(b, "BeginPlaceDown");
        Assert.IsFalse((bool)P(a, "IsPlacingDown")); Assert.IsTrue((bool)P(b, "IsPlacingDown"));
        foreach (var manager in new[] { m2 })
        {
            Expect(Context(manager), "Phase", "Placing");
            Assert.AreEqual("Base Layer.Pickup", P(Result(manager), "BaseAnimationState"));
            Assert.IsNotNull(P(Result(manager), "GripProvider"), "Placement retains hand contact until ownership releases");
        }
        for (float until = Time.time + 5f; ((bool)P(a, "IsPickingUp") || (bool)P(b, "IsPickingUp")) && Time.time < until;) yield return null;
        Assert.IsNull(P(a, "HeldObject")); Assert.IsNull(P(b, "HeldObject"));
        Empty(m1); Empty(m2);
        Assert.IsFalse((bool)P(a, "ExternalInteractionLocked")); Assert.IsFalse((bool)P(b, "ExternalInteractionLocked"));
    }

    [UnityTest]
    public IEnumerator HoseContextUsesOwnedRolesAndPreservesPressurePose()
    {
        EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return Ready();
        var p1 = GameObject.Find("Player1"); var p2 = GameObject.Find("Player2");
        var a = Actor(p1); var b = Actor(p2); var m1 = Manager(p1); var m2 = Manager(p2);
        var nozzle = GameObject.Find("HoseNozzle"); var rear = GameObject.Find("Hose_RearGrip");
        var hose = C(nozzle, Prefix + "CooperativeFireHose");
        Position(p2, new Vector3(nozzle.transform.position.x, .35f, nozzle.transform.position.z - .45f), Quaternion.identity);
        Call(b, "PickUpObject", C(nozzle, Prefix + "CooperativeHoseGrip"));
        Position(p1, (Vector3)Call(hose, "RearStance"), Quaternion.identity);
        Call(a, "PickUpObject", C(rear, Prefix + "CooperativeHoseGrip"));
        yield return Wait(1.2f);
        Assert.IsTrue((bool)P(hose, "ReadyToSpray"));
        Expect(Context(m2), "Role", "Front"); Expect(Context(m1), "Role", "Back");
        foreach (var manager in new[] { m1, m2 })
        {
            Expect(Context(manager), "InteractionType", "FireHoseInteraction"); Expect(Context(manager), "ObjectType", "Hose");
            Expect(Context(manager), "Phase", "Carrying");
            Assert.IsTrue((bool)P(Result(manager), "UsesHosePose"));
            Assert.AreEqual("Hose Layer.HoseHold", P(Result(manager), "BaseAnimationState"));
            HoseCheck("CheckMovingContacts", manager.gameObject);
        }
        for (int i = 0; i < 20; i++) Call(hose, "StepPressure", true, .1f);
        yield return null;
        float pressure = (float)P(hose, "Pressure"); Assert.Greater(pressure, .3f);
        var frontLean = p2.GetComponentInChildren(T(Prefix + "HeldObjectBodyLeanController"));
        var rearLean = p1.GetComponentInChildren(T(Prefix + "HeldObjectBodyLeanController"));
        Assert.That((float)F(frontLean, "targetAngle"), Is.EqualTo(pressure * 9f).Within(.2f));
        Assert.That((float)F(rearLean, "targetAngle"), Is.EqualTo(pressure * 4f).Within(.2f));
        Call(b, "ReleaseHeldObject");
        Assert.IsNull(P(a, "HeldObject")); Assert.IsNull(P(b, "HeldObject"));
        Empty(m1); Empty(m2);
    }

    [UnityTest]
    public IEnumerator ObjectInteractionContextTracksReservationAndClearsOnCompletionOrDisable()
    {
        EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return Ready();
        var player = GameObject.Find("Player1"); var manager = Manager(player);
        Position(GameObject.Find("Player2"), new Vector3(30f, .35f, 30f), Quaternion.identity);
        var controller = C(player, "HPlayer.DoorInteractionController");
        var lamp = C(GameObject.Find("Adaptive_Lamp"), "HGame.Objects.LampInteractable");
        var point = (Transform)Call(lamp, "GetInteractionPoint", 0);
        Position(player, point.position - point.right * .2f, point.rotation);
        yield return Wait(.3f);
        bool before = (bool)P(lamp, "IsOn");
        Call(controller, "TryInteract");
        Assert.AreSame(lamp, P(controller, "ActiveTarget"));
        var context = Context(manager);
        Expect(context, "InteractionType", "ObjectInteraction"); Expect(context, "Phase", "ObjectInteraction"); Expect(context, "Role", "None");
        Assert.AreSame(lamp, P(context, "ObjectTarget"));
        Assert.IsNull(P(context, "HeldObject")); Assert.IsNull(P(Result(manager), "GripProvider"));
        var adaptedLamp = lamp.GetComponentInParent(T(Prefix + "AdaptiveSceneProp"));
        Assert.IsNotNull(adaptedLamp);
        Expect(context, "ObjectType", "Box"); // Existing box-style carry provider on this normalized prop.
        Assert.Less(Vector3.Distance(adaptedLamp.GetComponent<BoxCollider>().size, (Vector3)P(context, "ObjectSize")), .001f);
        Assert.AreEqual(P(C(adaptedLamp.gameObject, Prefix + "CarryAnimationLoad"), "WeightKg"), P(context, "ObjectWeightKg"),
            "The switch must read the same adapted metadata as carrying the lamp");
        Assert.AreEqual("Base Layer.DoorPush", P(Result(manager), "BaseAnimationState"));
        for (float until = Time.time + 10f; (bool)P(controller, "IsInteractingWithObject") && Time.time < until;) yield return null;
        Assert.AreEqual(!before, P(lamp, "IsOn"));
        Empty(manager);
        yield return Wait(.6f);
        Call(controller, "TryInteract");
        Assert.AreSame(lamp, P(Context(manager), "ObjectTarget"));
        ((Behaviour)controller).enabled = false;
        Empty(manager);
        Assert.IsFalse((bool)P(Actor(player), "ExternalInteractionLocked"));
        ((Behaviour)controller).enabled = true;
        yield return Wait(.4f);
        var door = UnityEngine.Object.FindObjectsOfType(T("HGame.Objects.DoorInteractable")).Cast<Component>().First();
        point = (Transform)Call(door, "GetInteractionPoint", 0);
        Position(player, point.position, point.rotation);
        yield return Wait(.4f);
        Call(controller, "TryInteract");
        Assert.AreSame(door, P(controller, "ActiveTarget"));
        Expect(Context(manager), "InteractionType", "ObjectInteraction");
        Assert.AreSame(door, P(Context(manager), "ObjectTarget"));
        Assert.That((string)P(Result(manager), "BaseAnimationState"), Is.EqualTo("Base Layer.DoorPush").Or.EqualTo("Base Layer.DoorPull"));
        for (float until = Time.time + 10f; (bool)P(controller, "IsInteractingWithObject") && Time.time < until;) yield return null;
        Assert.IsTrue((bool)P(door, "IsOpen"));
        Empty(manager);
    }
}
