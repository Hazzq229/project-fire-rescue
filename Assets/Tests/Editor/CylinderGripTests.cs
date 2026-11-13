using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// Run the saved destination setup through its actual player/Animator pipeline.
// Reflection keeps this editor test assembly independent of Assembly-CSharp.
public class CylinderGripTests
{
    const string Scene = "Assets/Scenes/Gameloop_Demo.unity";
    const string Prefix = "TA.AdaptiveAnimation.";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static Component C(GameObject go, string name)
    {
        Assert.IsNotNull(go, "Missing GameObject while reading " + name);
        var component = go.GetComponent(T(name));
        Assert.IsNotNull(component, go.name + " missing component " + name);
        return component;
    }
    static object P(object value, string name)
    {
        Assert.IsNotNull(value, "Missing property owner " + name);
        var property = value.GetType().GetProperty(name);
        Assert.IsNotNull(property, value.GetType().Name + " missing property " + name);
        return property.GetValue(value);
    }
    static object F(object value, string name)
    {
        for (Type type = value.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Flags);
            if (field != null) return field.GetValue(value);
        }
        throw new MissingFieldException(value.GetType().FullName, name);
    }
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethods(Flags)
        .First(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v)).Invoke(value, args);
    static object HoseCheck(string name, params object[] args) => typeof(CooperativeHoseTests)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    static void Keys(params string[] keys) => HoseCheck("KeyboardState", new object[] { keys });
    static IEnumerator Wait(float seconds) { for (float until = Time.time + seconds; Time.time < until;) yield return null; }
    static Component Actor(GameObject player) => C(player, "HPlayer.InteractionController");
    static Component Manager(GameObject player) => C(player, Prefix + "AdaptiveAnimationManager");
    static object Context(GameObject player) => P(Manager(player), "Context");
    static object Result(GameObject player) => P(Manager(player), "Result");
    static void Expect(object value, string property, string expected) => Assert.AreEqual(expected, P(value, property).ToString(), property);
    static void Position(GameObject go, Vector3 position, Quaternion rotation)
    {
        go.transform.SetPositionAndRotation(position, rotation);
        var body = go.GetComponent<Rigidbody>();
        Assert.IsNotNull(body, go.name + " missing Rigidbody while positioning");
        body.position = position; body.rotation = rotation;
        if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        Physics.SyncTransforms();
    }
    static IEnumerator Ready()
    {
        HoseCheck("ResetKeyboard");
        var adapter = C(GameObject.Find("FR_Level_1"), Prefix + "AdaptiveSceneProps");
        for (float until = Time.time + 3f; !(bool)P(adapter, "IsReady") && Time.time < until;) yield return null;
        Assert.IsTrue((bool)P(adapter, "IsReady"));
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "CylinderGripTestFloor";
        floor.transform.position = new Vector3(30f, -.1f, 30f);
        floor.transform.localScale = new Vector3(20f, .2f, 20f);
        Physics.SyncTransforms();
        yield return Wait(.2f);
        Assert.IsNotNull(GameObject.Find("Player1"), "Destination must spawn Player1 before cylinder checks");
        Assert.IsNotNull(GameObject.Find("Player2"), "Destination must spawn Player2 before cylinder checks");
        Assert.IsNotNull(GameObject.Find("Prop_FireExtinguisher"), "Saved cylinder missing; providers=" +
            string.Join(",", UnityEngine.Object.FindObjectsOfType(T(Prefix + "CylinderGripProvider")).Select(o => o.name)));
    }
    static Component Provider(GameObject item)
    {
        var provider = C(item, Prefix + "CylinderGripProvider");
        Assert.IsNotNull(provider, "Saved extinguisher must explicitly opt into cylinder grip");
        Assert.IsTrue((bool)P(provider, "HasSurfaceGeometry"));
        Assert.AreEqual("cylinder-side", P(provider, "GripStyleId"));
        return provider;
    }
    static object[] Points(Component provider, Component actor)
    {
        var args = new object[] { actor, Vector3.zero, Vector3.zero };
        var method = provider.GetType().GetMethod("TryGetGripPoints", new[] {
            T("HPlayer.InteractionController"), typeof(Vector3).MakeByRefType(), typeof(Vector3).MakeByRefType() });
        Assert.IsTrue((bool)method.Invoke(provider, args), "Cylinder must provide valid mesh surface contacts");
        return args;
    }
    static object[] Rotations(Component provider, Component actor)
    {
        var args = new object[] { actor, Quaternion.identity, Quaternion.identity };
        var method = provider.GetType().GetMethod("TryGetGripRotations", new[] {
            T("HPlayer.InteractionController"), typeof(Quaternion).MakeByRefType(), typeof(Quaternion).MakeByRefType() });
        Assert.IsTrue((bool)method.Invoke(provider, args), "Cylinder must provide a palm frame for each curved surface contact");
        return args;
    }
    static Transform VisibleHand(GameObject player, string side)
    {
        var visuals = (GameObject)F(C(player, Prefix + "CharacterModelSlot"), "generatedVisuals");
        return visuals.GetComponentsInChildren<Transform>().First(t => t.name == "hand." + side);
    }
    static object RotationProfile(Component grip)
    {
        var main = F(grip, "rotationProfile");
        if (main != null && (bool)Call(main, "Matches", "cylinder-side")) return main;
        foreach (var profile in ((Array)F(grip, "additionalRotationProfiles")).Cast<object>())
            if (profile != null && (bool)Call(profile, "Matches", "cylinder-side")) return profile;
        Assert.Fail("The spawned rig needs a calibrated cylinder-side rotation profile");
        return null;
    }
    static void CheckContacts(GameObject player, GameObject item, Component provider)
    {
        var grip = player.GetComponentInChildren(T(Prefix + "HeldObjectGripController"));
        Assert.Greater((float)F(grip, "currentWeight"), .99f, player.name + " cylinder IK must stay active");
        var points = Points(provider, Actor(player));
        var frames = Rotations(provider, Actor(player));
        bool swap = (bool)F(grip, "swapHands");
        var profile = RotationProfile(grip);
        Vector3 leftPoint = (Vector3)points[swap ? 2 : 1], rightPoint = (Vector3)points[swap ? 1 : 2];
        Assert.Greater(Vector3.Dot(rightPoint - leftPoint, player.transform.right), .001f, "Cylinder grips must not cross the hands");
        foreach (string side in new[] { "L", "R" })
        {
            bool left = side == "L";
            var hand = VisibleHand(player, side);
            Vector3 targetPoint = left ? leftPoint : rightPoint;
            Assert.Less(Vector3.Distance(hand.position, targetPoint), .04f, player.name + " visible hand tracks cylinder contact " + side);
            Assert.Less(MeshDistance(item, hand.position), .025f, player.name + " visible hand touches actual cylinder mesh " + side);
            var data = F(F(grip, left ? "leftArmIK" : "rightArmIK"), "m_Data");
            var target = (Transform)P(data, "target");
            var frame = (Quaternion)frames[left ? (swap ? 2 : 1) : (swap ? 1 : 2)];
            var correction = (Quaternion)P(profile, left ? "LeftCorrection" : "RightCorrection");
            Assert.Less(Quaternion.Angle(target.rotation, frame * correction), 1f,
                "Palm orientation must use the cylinder surface frame rather than stale box rotation");
        }
    }
    static void CheckLegacyAttachment(GameObject player, GameObject item, float mass)
    {
        var body = item.GetComponent<Rigidbody>();
        Assert.IsFalse(body.isKinematic, "Cylinder grip must preserve the existing dynamic tool carry mode");
        Assert.That(body.mass, Is.EqualTo(mass).Within(.0001f));
        var joint = player.GetComponents<SpringJoint>().Single(j => j.connectedBody == body);
        Assert.That(joint.spring, Is.EqualTo((float)F(Actor(player), "jointSpring")).Within(.001f));
        Assert.That(joint.damper, Is.EqualTo((float)F(Actor(player), "jointDamper")).Within(.001f));
        Assert.IsFalse(joint.enableCollision);
    }
    static IEnumerator Pickup(GameObject player, GameObject item, Component provider)
    {
        var actor = Actor(player); var liftable = C(item, "HInteractions.Liftable");
        Call(actor, "BeginPickup", liftable);
        Assert.IsTrue((bool)P(actor, "IsPickingUp"));
        Expect(Context(player), "ObjectType", "Cylinder");
        Expect(Context(player), "Phase", "Preparing");
        Assert.IsNull(P(Result(player), "GripProvider"), "Preparing pickup must wait for the actual grab event");
        for (float until = Time.time + 5f; (bool)P(actor, "IsPickingUp") && Time.time < until;) yield return null;
        Assert.AreSame(liftable, P(actor, "HeldObject"), "Pickup_Grab event must claim the existing tool");
        yield return Wait(1.5f);
        Expect(Context(player), "ObjectType", "Cylinder");
        Expect(Context(player), "InteractionType", "CarryObject");
        Expect(Context(player), "Role", "None");
        Expect(Context(player), "Phase", "Carrying");
        Assert.AreSame(provider, P(Result(player), "GripProvider"));
        Assert.IsFalse((bool)P(Result(player), "UsesHosePose"));
        Assert.AreEqual("Carry Layer.CarryHold", P(Result(player), "BaseAnimationState"));
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
    public IEnumerator SavedCylinderPickupContactsScaleAndWeightForBothPlayers()
    {
        EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return Ready();
        var item = GameObject.Find("Prop_FireExtinguisher"); var provider = Provider(item);
        var load = C(item, Prefix + "CarryAnimationLoad"); Assert.IsNotNull(load);
        var water = C(item, "HGame.Objects.HoseWaterController"); Assert.IsNotNull(water);
        var originalFX = F(water, "waterFX"); var originalOrigin = F(water, "raycastOrigin");
        Vector3 scale = item.transform.localScale; Quaternion rotation = item.transform.rotation;
        float mass = item.GetComponent<Rigidbody>().mass;
        foreach (int slot in new[] { 0, 1 })
        {
            var player = GameObject.Find(slot == 0 ? "Player1" : "Player2");
            var other = GameObject.Find(slot == 0 ? "Player2" : "Player1");
            Position(other, new Vector3(35f, .35f, 28f), Quaternion.identity);
            var factors = new[] { .8f, 1f, 1.4f }; var weights = new[] { 1f, 10f, 30f };
            for (int sample = 0; sample < factors.Length; sample++)
            {
                Position(player, new Vector3(30f, .35f, 28f), Quaternion.identity);
                item.transform.localScale = scale * factors[sample];
                Position(item, new Vector3(30f, .8f, 28.55f), rotation);
                Call(load, "Configure", weights[sample]);
                yield return Wait(.3f);
                yield return Pickup(player, item, provider);
                Assert.That((float)P(Context(player), "ObjectWeightKg"), Is.EqualTo(weights[sample]).Within(.001f));
                Assert.That((float)P(Context(player), "EffectiveWeightKg"), Is.EqualTo(weights[sample]).Within(.001f));
                Assert.Less(Vector3.Distance((Vector3)P(provider, "ObjectDimensions"), (Vector3)P(Context(player), "ObjectSize")), .001f);
                var lean = player.GetComponentInChildren(T(Prefix + "HeldObjectBodyLeanController"));
                Assert.That((float)F(lean, "currentLoadKg"), Is.EqualTo(weights[sample]).Within(.01f));
                CheckLegacyAttachment(player, item, mass);
                for (int frame = 0; frame < 8; frame++) { yield return null; CheckContacts(player, item, provider); }
                if (sample == factors.Length - 1)
                {
                    Vector3 before = player.transform.position;
                    Keys(slot == 0 ? "D" : "RightArrow"); yield return Wait(.5f);
                    for (int frame = 0; frame < 8; frame++) { yield return null; CheckContacts(player, item, provider); }
                    Keys(); yield return Wait(.3f);
                    Assert.Greater(Vector3.ProjectOnPlane(player.transform.position - before, Vector3.up).magnitude, .15f);
                    CooperativeHoseTests.Capture("Temp/Gameloop-Cylinder-P" + (slot + 1) + ".png", player.transform.position + Vector3.up * .5f);
                }
                Assert.AreSame(water, C(item, "HGame.Objects.HoseWaterController"));
                Assert.AreEqual(originalFX, F(water, "waterFX")); Assert.AreEqual(originalOrigin, F(water, "raycastOrigin"));
                Assert.IsFalse((bool)P(water, "IsShooting"), "Grip adaptation must not toggle the existing tool");
                Call(Actor(player), "ReleaseHeldObject"); yield return Wait(.8f);
                Expect(Context(player), "InteractionType", "None");
                Assert.IsNull(P(Result(player), "GripProvider"));
                Assert.Less((float)F(player.GetComponentInChildren(T(Prefix + "HeldObjectGripController")), "currentWeight"), .01f);
            }
        }
    }

    [UnityTest]
    public IEnumerator CylinderSurfaceGeometryTracksYawTiltAndNonuniformScale()
    {
        EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return Ready();
        var item = GameObject.Find("Prop_FireExtinguisher"); var provider = Provider(item);
        var player = GameObject.Find("Player1"); Position(player, new Vector3(30f, .35f, 28f), Quaternion.identity);
        Position(GameObject.Find("Player2"), new Vector3(35f, .35f, 28f), Quaternion.identity);
        var body = item.GetComponent<Rigidbody>(); body.isKinematic = true;
        var filter = item.GetComponent<MeshFilter>(); Assert.IsTrue(filter.sharedMesh.isReadable);
        Vector3 originalScale = item.transform.localScale; Quaternion originalRotation = item.transform.rotation;
        float smallestSpacing = 0f, largestSpacing = 0f;
        foreach (Vector3 factor in new[] { Vector3.one * .8f, Vector3.one, Vector3.one * 1.4f, new Vector3(1.4f, .8f, 1.2f) })
        {
            item.transform.localScale = Vector3.Scale(originalScale, factor);
            Position(item, new Vector3(30f, 1f, 29f), originalRotation);
            Vector3 dimensions = (Vector3)P(provider, "ObjectDimensions");
            foreach (float yaw in new[] { 0f, 65f, 135f })
            {
                Position(item, new Vector3(30f, 1f, 29f), Quaternion.Euler(12f, yaw, 8f) * originalRotation);
                var points = Points(provider, Actor(player)); var frames = Rotations(provider, Actor(player));
                Vector3 a = (Vector3)points[1], b = (Vector3)points[2];
                Assert.Less(MeshDistance(item, a), .001f); Assert.Less(MeshDistance(item, b), .001f);
                float spacing = Vector3.Distance(a, b);
                Assert.That(spacing, Is.InRange(.002f, .281f), "Curved grips must remain distinct and within the configured hand span");
                Assert.Greater(Vector3.Dot(b - a, player.transform.right), .001f);
                Assert.Less(Vector3.Distance(dimensions, (Vector3)P(provider, "ObjectDimensions")), .0001f,
                    "Yaw must not change the local size parameter");
                Vector3 axis = filter.transform.TransformDirection(Vector3.forward).normalized;
                Vector3 center = filter.transform.TransformPoint(filter.sharedMesh.bounds.center);
                for (int side = 1; side <= 2; side++)
                {
                    Vector3 inward = Vector3.ProjectOnPlane(center - (Vector3)points[side], axis).normalized;
                    Vector3 palmInward = (Quaternion)frames[side] * Vector3.forward;
                    Assert.Greater(Vector3.Dot(palmInward, inward), .65f,
                        "Surface frame must point the palm into the curved body after scale/rotation");
                }
                if (yaw == 0f && factor == Vector3.one * .8f) smallestSpacing = spacing;
                if (yaw == 0f && factor == Vector3.one * 1.4f) largestSpacing = spacing;
            }
        }
        Assert.Greater(largestSpacing, smallestSpacing + .015f, "A larger barrel must produce wider contacts before reaching the span cap");
    }

    [UnityTest]
    public IEnumerator CylinderProviderDisableAndPlacementPreserveToolCleanup()
    {
        EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return Ready();
        var item = GameObject.Find("Prop_FireExtinguisher"); var provider = Provider(item);
        var player = GameObject.Find("Player1"); var actor = Actor(player);
        Position(player, new Vector3(30f, .35f, 28f), Quaternion.identity);
        Position(GameObject.Find("Player2"), new Vector3(35f, .35f, 28f), Quaternion.identity);
        Position(item, new Vector3(30f, .8f, 28.55f), item.transform.rotation);
        float mass = item.GetComponent<Rigidbody>().mass;
        yield return Wait(.3f); yield return Pickup(player, item, provider);
        CheckLegacyAttachment(player, item, mass); CheckContacts(player, item, provider);
        ((Behaviour)provider).enabled = false;
        Assert.IsNull(P(Result(player), "GripProvider"));
        yield return Wait(.4f);
        var grip = player.GetComponentInChildren(T(Prefix + "HeldObjectGripController"));
        Assert.Less((float)F(grip, "currentWeight"), .01f);
        Assert.IsNotNull(P(actor, "HeldObject"), "Disabling a grip provider must preserve existing ownership");
        ((Behaviour)provider).enabled = true;
        yield return Wait(.6f); CheckContacts(player, item, provider);
        Call(actor, "BeginPlaceDown");
        Assert.IsTrue((bool)P(actor, "IsPlacingDown")); Expect(Context(player), "Phase", "Placing");
        Assert.IsNotNull(P(Result(player), "GripProvider"), "Placement keeps the contact source until the event releases it");
        for (float until = Time.time + 5f; (bool)P(actor, "IsPickingUp") && Time.time < until;) yield return null;
        Assert.IsNull(P(actor, "HeldObject"), "Actual placement contact must release the tool");
        Expect(Context(player), "InteractionType", "None"); Assert.IsNull(P(Result(player), "GripProvider"));
        Assert.IsFalse(item.GetComponent<Rigidbody>().isKinematic);
        Assert.That(item.GetComponent<Rigidbody>().mass, Is.EqualTo(mass).Within(.0001f));
        Assert.IsFalse((bool)P(actor, "ExternalInteractionLocked"));
        yield return Wait(.8f);
        Assert.Less((float)F(grip, "currentWeight"), .01f);
        // Avoid a capturing lambda in an EnterPlayMode coroutine: its generated
        // display class can be lost when the test resumes after domain reload.
        foreach (var joint in player.GetComponents<SpringJoint>())
            Assert.AreNotEqual(item.GetComponent<Rigidbody>(), joint.connectedBody);
        Assert.IsFalse((bool)P(C(item, "HGame.Objects.HoseWaterController"), "IsShooting"));
    }

    static float MeshDistance(GameObject item, Vector3 point)
    {
        float distance = float.PositiveInfinity;
        foreach (var filter in item.GetComponentsInChildren<MeshFilter>())
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (!renderer || !renderer.enabled || !filter.sharedMesh) continue;
            var mesh = filter.sharedMesh;
            Assert.IsTrue(mesh.isReadable, "Cylinder surface validation requires the actual visible mesh");
            var vertices = mesh.vertices; var triangles = mesh.triangles;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = filter.transform.TransformPoint(vertices[i]);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
                distance = Mathf.Min(distance, Vector3.Distance(point, ClosestTriangle(point,
                    vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]])));
        }
        return distance;
    }
    static Vector3 ClosestTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        if (Vector3.Cross(ab, ac).sqrMagnitude <= 1e-20f) return DegenerateTriangle(p, a, b, c);
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float sum = va + vb + vc;
        if (sum > 0f) return a + ab * (vb / sum) + ac * (vc / sum);
        // Tiny valid cylinder facets must not be mistaken for degenerate faces.
        // For a truly degenerate face, compare the three segments instead.
        return DegenerateTriangle(p, a, b, c);
    }
    static Vector3 DegenerateTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 x = Segment(p, a, b), y = Segment(p, b, c), z = Segment(p, c, a);
        return (p - x).sqrMagnitude < (p - y).sqrMagnitude
            ? ((p - x).sqrMagnitude < (p - z).sqrMagnitude ? x : z)
            : ((p - y).sqrMagnitude < (p - z).sqrMagnitude ? y : z);
    }
    static Vector3 Segment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 delta = b - a;
        return delta.sqrMagnitude > 0f ? a + delta * Mathf.Clamp01(Vector3.Dot(point - a, delta) / delta.sqrMagnitude) : a;
    }
}
