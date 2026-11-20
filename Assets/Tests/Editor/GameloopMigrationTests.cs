using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// Exercise the destination scene's saved setup, rather than the adaptive demo fixtures.
public class GameloopMigrationTests
{
    const string Scene = "Assets/Scenes/Gameloop_Demo.unity";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static Component C(GameObject go, string type) => go.GetComponent(T(type));
    static object P(object o, string name) => o.GetType().GetProperty(name).GetValue(o);
    static object F(object o, string name) => o.GetType().GetField(name, Flags).GetValue(o);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethods(Flags)
        .First(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v)).Invoke(o, args);
    static object HoseCheck(string name, params object[] args) => typeof(CooperativeHoseTests)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    static void Keys(params string[] keys) => HoseCheck("KeyboardState", new object[] { keys });
    static void Position(GameObject go, Vector3 point, Quaternion rotation)
    {
        go.transform.SetPositionAndRotation(point, rotation);
        var body = go.GetComponent<Rigidbody>();
        body.position = point; body.rotation = rotation;
        if (!body.isKinematic) body.velocity = Vector3.zero;
        Physics.SyncTransforms();
    }
    static IEnumerator Wait(float seconds)
    {
        for (float until = Time.time + seconds; Time.time < until;) yield return null;
    }
    static IEnumerator WaitInteraction(Component controller)
    {
        for (float until = Time.time + 10f; (bool)P(controller, "IsInteractingWithObject") && Time.time < until;) yield return null;
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
    public IEnumerator MenuKeyboardJoinPreservesAssignmentsIntoGameloop()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        yield return new EnterPlayMode();
        HoseCheck("ResetKeyboard");
        yield return Wait(.4f);
        var menu = (Component)UnityEngine.Object.FindObjectOfType(T("MainMenuUIManager"));
        Assert.NotNull(menu);
        Call(menu, "ShowCharacterSelect");
        Keys("A"); yield return Wait(.2f); Keys(); yield return Wait(.2f);
        Keys("LeftArrow"); yield return Wait(.2f); Keys(); yield return Wait(.2f);
        Assert.IsTrue((bool)T("LocalCoopSession").GetMethod("IsJoined").Invoke(null, new object[] { 0 }));
        Assert.IsTrue((bool)T("LocalCoopSession").GetMethod("IsJoined").Invoke(null, new object[] { 1 }));
        Call(menu, "StartGame");
        yield return Wait(.6f);
        Assert.AreEqual("Gameloop_IJUN", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        Assert.IsTrue((bool)P(C(GameObject.Find("FR_Level_1"), "TA.AdaptiveAnimation.AdaptiveSceneProps"), "IsReady"));
        var basePlayer = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        var baseController = basePlayer.GetComponent<Animator>().runtimeAnimatorController;
        foreach (string name in new[] { "Player1", "Player2" })
        {
            var player = GameObject.Find(name); Assert.NotNull(player);
            bool isSecond = name == "Player2";
            var input = C(player, "UnityEngine.InputSystem.PlayerInput");
            Assert.AreEqual(isSecond ? "KeyboardArrows" : "KeyboardWASD", P(input, "currentControlScheme"));
            Assert.AreEqual("Player", P(P(input, "currentActionMap"), "name"));
            Assert.IsNotNull(C(player, "ThirdPersonPlayerController"));
            var animator = player.GetComponent<Animator>();
            Assert.AreSame(baseController, animator.runtimeAnimatorController);

            var slot = C(player, "TA.AdaptiveAnimation.CharacterModelSlot");
            Assert.AreEqual(isSecond ? "Kucing" : "Rubah_P1", ((GameObject)F(slot, "appliedModel")).name);
            string helmetName = (string)F(slot, "headgearMeshName");
            var appearance = (GameObject)F(slot, "generatedVisuals");
            Assert.AreSame(animator, F(C(appearance, "TA.AdaptiveAnimation.CharacterModelAnimatorMirror"), "sourceAnimator"));
            var skins = appearance.GetComponentsInChildren<SkinnedMeshRenderer>();
            var helmet = skins.Single(r => r.name == helmetName);
            Assert.IsTrue(helmet.enabled);
            Assert.IsNotNull(helmet.sharedMesh);
            if (isSecond)
            {
                var eyes = skins.Single(r => r.name == "Kucing Rig | Mata");
                Assert.IsTrue(eyes.enabled);
                CollectionAssert.AreEquivalent(new[] { "Kucing_Sclera", "Kucing_Iris", "Kucing_Pupil" },
                    eyes.sharedMaterials.Select(material => material.name));
                Assert.IsTrue(eyes.sharedMaterials.All(material => material.GetFloat("_Cull") == 0f),
                    "The cat must inherit its two-sided eye materials from the imported model");
            }
        }

        var badak = CheckLevelSurvivors();
        var start = badak.transform.position;
        float deadline = Time.time + 8f;
        while (Vector3.ProjectOnPlane(badak.transform.position - start, Vector3.up).sqrMagnitude < .25f * .25f &&
               Time.time < deadline)
            yield return null;
        Assert.That(Vector3.ProjectOnPlane(badak.transform.position - start, Vector3.up).magnitude,
            Is.GreaterThanOrEqualTo(.25f), "Badak must move on the authored NavMesh");
    }

    static Behaviour CheckLevelSurvivors()
    {
        var victims = UnityEngine.Object.FindObjectsOfType(T("VictimAI")).Cast<Behaviour>().ToArray();
        Assert.AreEqual(4, victims.Length);
        var manager = UnityEngine.Object.FindObjectOfType(T("GameManager"));
        CollectionAssert.AreEquivalent(victims, (IList)F(manager, "activeVictims"));

        foreach (var victim in victims) Assert.IsTrue(victim.isActiveAndEnabled, victim.name);
        CollectionAssert.AreEquivalent(new[] { "TupaiSurvivor", "TupaiSurvivor", "TupaiSurvivor", "BadakSurvivor" },
            victims.Select(victim => victim.GetComponentInChildren<Animator>().runtimeAnimatorController.name));
        var badak = victims.Single(victim => victim.name == "Badak - Survivor");
        var agent = badak.GetComponent<UnityEngine.AI.NavMeshAgent>();
        Assert.IsTrue(agent && agent.enabled && agent.isOnNavMesh, "Badak must bind to the interior NavMesh");
        var animator = badak.GetComponentInChildren<Animator>();
        CollectionAssert.AreEquivalent(new[] { "Idle", "Jalan Santai", "Lari Panik", "Sesak lalu Pingsan", "Sadar Kembali" },
            animator.runtimeAnimatorController.animationClips.Select(clip => clip.name));

        var camera = Camera.main;
        Assert.NotNull(camera, "The shared gameplay camera must render the initial view for both players");
        const float margin = .04f;
        var capsule = badak.GetComponent<CapsuleCollider>();
        Vector3 head = capsule.bounds.center + Vector3.up * (capsule.bounds.extents.y * .78f);
        Vector3 viewport = camera.WorldToViewportPoint(head);
        Assert.Greater(viewport.z, camera.nearClipPlane, "Badak camera depth");
        Assert.That(viewport.x, Is.InRange(margin, 1f - margin), "Badak horizontal viewport position");
        Assert.That(viewport.y, Is.InRange(margin, 1f - margin), "Badak vertical viewport position");
        return badak;
    }

    [UnityTest]
    public IEnumerator SpawnedPlayersKeepModelsMoveIndependentlyAndDash()
    {
        EditorSceneManager.OpenScene(Scene);
        yield return new EnterPlayMode();
        HoseCheck("ResetKeyboard");
        yield return Wait(.4f);
        var p1 = GameObject.Find("Player1"); var p2 = GameObject.Find("Player2");
        Assert.NotNull(p1); Assert.NotNull(p2);
        Assert.AreEqual(2, UnityEngine.Object.FindObjectsOfType(T("UnityEngine.InputSystem.PlayerInput")).Length);
        foreach (var player in new[] { p1, p2 })
        {
            var input = C(player, "UnityEngine.InputSystem.PlayerInput");
            Assert.AreEqual(player == p1 ? "KeyboardWASD" : "KeyboardArrows", P(input, "currentControlScheme"));
            Assert.AreEqual("Player", P(P(input, "currentActionMap"), "name"));
            var slot = C(player, "TA.AdaptiveAnimation.CharacterModelSlot");
            var visual = (GameObject)F(slot, "generatedVisuals");
            Assert.NotNull(visual);
            var skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>();
            var hat = skins.Single(r => r.name == "Topi_Pemadam");
            Assert.IsTrue(hat.enabled); Assert.NotNull(hat.sharedMesh);
            Assert.AreEqual("RigidHead_Topi_Pemadam", hat.sharedMesh.name);
            Assert.IsTrue(UnityEditor.AssetDatabase.Contains(hat.sharedMesh), "The fitted rigid helmet must remain a persistent asset");
            Assert.Greater(hat.sharedMesh.vertexCount, 100);
        }
        foreach (string key in new[] { "D", "RightArrow" })
        {
            Position(p1, new Vector3(9.3f, .35f, -22.9f), Quaternion.identity);
            Position(p2, new Vector3(11f, .35f, -22.9f), Quaternion.identity);
            yield return Wait(.2f);
            Vector3 a = p1.transform.position, b = p2.transform.position;
            Keys(key); yield return Wait(.65f); Keys();
            float d1 = Vector3.Distance(a, p1.transform.position), d2 = Vector3.Distance(b, p2.transform.position);
            Debug.Log("MIGRATION walk " + key + " P1=" + d1 + " P2=" + d2);
            Assert.Greater(key == "D" ? d1 : d2, .35f, "Paired movement must displace its player");
            Assert.Less(key == "D" ? d2 : d1, .08f, "Other player must stay still");
            yield return Wait(.5f);
            Assert.IsFalse(p1.GetComponent<Animator>().GetBool("isWalking"));
            Assert.IsFalse(p2.GetComponent<Animator>().GetBool("isWalking"));
        }
        foreach (string key in new[] { "LeftShift", "RightAlt" })
        {
            Position(p1, new Vector3(9.3f, .35f, -22.9f), Quaternion.LookRotation(Vector3.back));
            Position(p2, new Vector3(11f, .35f, -22.9f), Quaternion.LookRotation(Vector3.back));
            yield return Wait(.2f);
            var player = key == "LeftShift" ? p1 : p2;
            Vector3 before = player.transform.position;
            Keys(key); yield return Wait(.06f);
            Assert.IsTrue(player.GetComponent<Animator>().GetBool("isDashing"), key);
            Keys(); yield return Wait(.35f);
            float distance = Vector3.Distance(before, player.transform.position);
            Debug.Log("MIGRATION dash " + player.name + " distance=" + distance);
            Assert.Greater(distance, .8f);
            Assert.IsFalse(player.GetComponent<Animator>().GetBool("isDashing"));
            yield return Wait(.8f);
        }
    }

    [UnityTest]
    public IEnumerator AllDestinationDoorsOpenAwayAndCloseFromTheSamePosition()
    {
        EditorSceneManager.OpenScene(Scene);
        yield return new EnterPlayMode();
        HoseCheck("ResetKeyboard");
        var player = GameObject.Find("Player1");
        Position(GameObject.Find("Player2"), new Vector3(30f, .35f, 30f), Quaternion.identity);
        var controller = C(player, "HPlayer.DoorInteractionController");
        yield return Wait(.4f);
        var doors = UnityEngine.Object.FindObjectsOfType(T("HGame.Objects.DoorInteractable")).Cast<Component>().ToArray();
        Assert.AreEqual(7, doors.Length);
        var failures = new List<string>();
        foreach (var door in doors)
        {
            var leaf = (Rigidbody)F(door, "hingeBody");
            Quaternion closed = leaf.rotation;
            float firstSign = 0f;
            foreach (int side in new[] { 0, 1 })
            {
                var point = (Transform)Call(door, "GetInteractionPoint", side);
                Position(player, point.position, point.rotation);
                yield return Wait(.5f);
                Call(controller, "TryInteract");
                if (!ReferenceEquals(door, P(controller, "ActiveTarget")))
                {
                    failures.Add(door.name + " side=" + side + " cannot select/open");
                    continue;
                }
                yield return WaitInteraction(controller);
                if (!(bool)P(door, "IsOpen")) { failures.Add(door.name + " did not open"); continue; }
                float sign = Vector3.SignedAngle(closed * Vector3.forward, leaf.rotation * Vector3.forward, Vector3.up);
                if (side == 0) firstSign = sign;
                else if (firstSign * sign >= 0f) failures.Add(door.name + " does not reverse swing");
                Vector3 openingPosition = player.transform.position;
                yield return Wait(.6f);
                Call(controller, "TryInteract");
                if (!ReferenceEquals(door, P(controller, "ActiveTarget"))) failures.Add(door.name + " cannot close at opening position");
                yield return WaitInteraction(controller);
                if ((bool)P(door, "IsOpen")) failures.Add(door.name + " stays open");
                if (Vector3.Distance(openingPosition, player.transform.position) > .08f) failures.Add(door.name + " closing moves opener");
                yield return Wait(.5f);
            }
        }
        Debug.Log("MIGRATION door checks: " + string.Join("; ", failures));
        Assert.IsEmpty(failures);
    }

    [UnityTest]
    public IEnumerator DestinationLampTogglesThroughAnimationContact()
    {
        EditorSceneManager.OpenScene(Scene);
        yield return new EnterPlayMode();
        HoseCheck("ResetKeyboard");
        var lamp = C(GameObject.Find("Adaptive_Lamp"), "HGame.Objects.LampInteractable");
        var point = (Transform)Call(lamp, "GetInteractionPoint", 0);
        var light = lamp.GetComponentInChildren<Light>();
        float maxPenetration = 0f;
        foreach (int slot in new[] { 0, 1 })
        {
            var player = GameObject.Find(slot == 0 ? "Player1" : "Player2");
            var other = GameObject.Find(slot == 0 ? "Player2" : "Player1");
            Position(other, new Vector3(30f, .35f, 30f), Quaternion.identity);
            var controller = C(player, "HPlayer.DoorInteractionController");
            // Both approaches are beyond the previous .45 m reach, and neither
            // requires the player to face the precise animation anchor.
            Vector3 offset = slot == 0 ? new Vector3(-.6f, 0f, -.2f) : new Vector3(.3f, 0f, -.5f);
            Position(player, point.position + offset, Quaternion.LookRotation(Vector3.left));
            yield return Wait(.5f);
            Assert.Greater(Vector3.ProjectOnPlane(player.transform.position - point.position, Vector3.up).magnitude, .5f);
            maxPenetration = Mathf.Max(maxPenetration, LampPlayerPenetration(player));
            foreach (int attempt in new[] { 0, 1 })
            {
                bool before = (bool)P(lamp, "IsOn");
                Vector3 beforePosition = player.transform.position;
                Keys(slot == 0 ? "E" : "RightShift"); yield return null; yield return null; Keys();
                Assert.AreSame(lamp, P(controller, "ActiveTarget"), player.name + " attempt=" + attempt);
                Assert.AreEqual(before, P(lamp, "IsOn"), "Input reserves; the animation contact toggles");
                for (float until = Time.time + 10f; (bool)P(controller, "IsInteractingWithObject") && Time.time < until;)
                {
                    maxPenetration = Mathf.Max(maxPenetration, LampPlayerPenetration(player));
                    yield return null;
                }
                Assert.IsFalse((bool)P(controller, "IsInteractingWithObject"), "Must release the animation lock");
                Assert.AreEqual(!before, P(lamp, "IsOn"));
                Assert.AreEqual(!before, light.enabled);
                Assert.IsFalse((bool)P(C(player, "HPlayer.InteractionController"), "ExternalInteractionLocked"));
                Assert.Less(Vector3.ProjectOnPlane(player.transform.position - point.position, Vector3.up).magnitude, .04f);
                if (attempt == 1) Assert.Less(Vector3.Distance(beforePosition, player.transform.position), .04f,
                    "Switching back must work from the same settled position");
                yield return Wait(.6f);
            }
        }
        Debug.Log("MIGRATION lamp both keyboard slots: four contact toggles; max player penetration=" + maxPenetration);
    }

    static float LampPlayerPenetration(GameObject player)
    {
        var capsule = player.GetComponent<CapsuleCollider>();
        float maximum = 0f;
        foreach (var obstacle in Physics.OverlapBox(capsule.bounds.center, capsule.bounds.extents + Vector3.one * .02f,
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (obstacle.transform.IsChildOf(player.transform)) continue;
            if (!Physics.ComputePenetration(capsule, capsule.transform.position, capsule.transform.rotation,
                obstacle, obstacle.transform.position, obstacle.transform.rotation, out _, out float depth)) continue;
            maximum = Mathf.Max(maximum, depth);
            Assert.Less(depth, .008f, "Lamp alignment must not push into " + obstacle.name);
        }
        return maximum;
    }

    [UnityTest]
    public IEnumerator DestinationLampRejectsBlockedAndOutOfRangeApproaches()
    {
        EditorSceneManager.OpenScene(Scene);
        yield return new EnterPlayMode();
        HoseCheck("ResetKeyboard");
        var player = GameObject.Find("Player1");
        Position(GameObject.Find("Player2"), new Vector3(30f, .35f, 30f), Quaternion.identity);
        var lamp = C(GameObject.Find("Adaptive_Lamp"), "HGame.Objects.LampInteractable");
        var controller = C(player, "HPlayer.DoorInteractionController");
        var point = (Transform)Call(lamp, "GetInteractionPoint", 0);
        foreach (Vector3 offset in new[] { Vector3.back * 1.15f, Vector3.back * .7f })
        {
            Position(player, point.position + offset, point.rotation);
            GameObject blocker = null;
            if (offset.magnitude < 1f)
            {
                blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.name = "LampApproachTestWall";
                blocker.transform.position = point.position + Vector3.back * .15f + Vector3.up * .6f;
                blocker.transform.localScale = new Vector3(2f, 2f, .06f);
            }
            Physics.SyncTransforms();
            yield return Wait(.3f);
            bool before = (bool)P(lamp, "IsOn");
            Keys("E"); yield return Wait(.1f); Keys();
            Assert.IsNull(P(controller, "ActiveTarget"), "Reject " + (blocker ? "wall-obstructed" : "distant") + " interaction");
            Assert.AreEqual(before, P(lamp, "IsOn"));
            Assert.IsFalse((bool)P(C(player, "HPlayer.InteractionController"), "ExternalInteractionLocked"));
            if (blocker) UnityEngine.Object.Destroy(blocker);
            yield return null;
        }
        // This lamp is also a level prop. Its switch must not reserve a moving,
        // carried stand point while the partner has ownership of the prop.
        Position(player, point.position, point.rotation);
        var interaction = C(player, "HPlayer.InteractionController");
        var liftable = lamp.GetComponentInParent(T("HInteractions.Liftable"));
        Call(interaction, "PickUpObject", liftable);
        Assert.IsTrue((bool)P(liftable, "IsLifted"));
        Assert.IsFalse((bool)P(lamp, "Available"));
        Assert.IsFalse((bool)Call(lamp, "TryReserve", controller, !(bool)P(lamp, "IsOn")));
        Call(interaction, "ReleaseHeldObject");
    }

    static void PickUpHose()
    {
        var p1 = GameObject.Find("Player1"); var p2 = GameObject.Find("Player2");
        var front = GameObject.Find("HoseNozzle");
        var hose = C(front, "TA.AdaptiveAnimation.CooperativeFireHose");
        Position(p1, new Vector3(front.transform.position.x, .35f, front.transform.position.z - .45f), Quaternion.identity);
        Call(C(p1, "HPlayer.InteractionController"), "PickUpObject", C(front, "TA.AdaptiveAnimation.CooperativeHoseGrip"));
        Position(p2, (Vector3)Call(hose, "RearStance"), Quaternion.identity);
        Call(C(p2, "HPlayer.InteractionController"), "PickUpObject", C(GameObject.Find("Hose_RearGrip"), "TA.AdaptiveAnimation.CooperativeHoseGrip"));
    }

    [UnityTest]
    public IEnumerator DestinationHoseMaintainsVisibleContactsAndClearance()
    {
        EditorSceneManager.OpenScene(Scene);
        yield return new EnterPlayMode();
        HoseCheck("ResetKeyboard");
        yield return Wait(.3f);
        PickUpHose(); yield return Wait(1.5f);
        var hose = C(GameObject.Find("HoseNozzle"), "TA.AdaptiveAnimation.CooperativeFireHose");
        Assert.IsTrue((bool)P(hose, "ReadyToSpray"));
        foreach (string name in new[] { "Player1", "Player2" })
        {
            HoseCheck("CheckMovingContacts", GameObject.Find(name));
            HoseCheck("CheckNozzleGeometry", GameObject.Find(name), GameObject.Find("HoseNozzle"));
        }
        HoseCheck("CheckStraightSpan", hose);
        var player = GameObject.Find("Player1"); Vector3 before = player.transform.position;
        Keys("W"); yield return Wait(.7f);
        Assert.Greater(Vector3.Distance(before, player.transform.position), .15f);
        foreach (string name in new[] { "Player1", "Player2" }) HoseCheck("CheckMovingContacts", GameObject.Find(name));
        Keys(); yield return Wait(.6f);
        Assert.IsFalse(player.GetComponent<Animator>().GetBool("isWalking"));
    }

    [UnityTest]
    public IEnumerator DestinationHosePressureReelAndExtinguishing()
    {
        EditorSceneManager.OpenScene(Scene);
        yield return new EnterPlayMode();
        HoseCheck("ResetKeyboard");
        yield return Wait(.3f);
        PickUpHose(); yield return Wait(1f);
        var front = GameObject.Find("HoseNozzle");
        var hose = C(front, "TA.AdaptiveAnimation.CooperativeFireHose");
        var water = C(front, "HGame.Objects.HoseWaterController");
        Assert.IsTrue((bool)P(hose, "ReadyToSpray"));
        // The saved hose initially faces a solid level wall. Turn onto the open road.
        Keys("L"); yield return Wait(1.6f); Keys(); yield return Wait(.5f);
        Assert.IsTrue((bool)P(hose, "ReadyToSpray"));
        for (int i = 0; i < 35; i++) Call(hose, "StepPressure", true, .1f);
        Assert.That((float)P(hose, "Pressure"), Is.EqualTo(.7f).Within(.01f));
        Assert.IsTrue((bool)P(water, "IsShooting"));
        float length = (float)P(hose, "HoseLength");
        Call(hose, "ToggleReel", C(GameObject.Find("Player2"), "HPlayer.InteractionController"), 1);
        Call(hose, "StepReel", 1f);
        Assert.Greater((float)P(hose, "HoseLength"), length);
        var origin = (Transform)F(water, "raycastOrigin");
        Assert.Less(Vector3.Angle(origin.forward, (Vector3)P(hose, "AimDirection")), 1f, "Muzzle must match aim");
        var fireObject = new GameObject("MigrationTestFire");
        fireObject.transform.position = origin.position + origin.forward * 1.2f - Vector3.up * .35f;
        var shape = fireObject.AddComponent<BoxCollider>(); shape.isTrigger = true; shape.size = Vector3.one;
        var fire = fireObject.AddComponent(T("Fire"));
        Physics.SyncTransforms();
        for (int i = 0; i < 100; i++) Call(water, "HandleFireExtinguishing");
        Assert.LessOrEqual((float)Call(fire, "GetIntensity"), 0f, "ray=" + origin.position + " aim=" + origin.forward +
            " hits=" + string.Join(",", Physics.RaycastAll(origin.position, origin.forward, 3f, ~0, QueryTriggerInteraction.Collide)
                .OrderBy(h => h.distance).Select(h => h.collider.name + ":" + h.distance + ":" + h.collider.isTrigger)));
        for (int i = 0; i < 30; i++) Call(hose, "StepPressure", false, .1f);
        Assert.IsFalse((bool)P(water, "IsShooting"));
        Call(C(GameObject.Find("Player1"), "HPlayer.InteractionController"), "ReleaseHeldObject");
        Assert.IsNull(P(C(GameObject.Find("Player2"), "HPlayer.InteractionController"), "HeldObject"));
    }

    [UnityTest]
    public IEnumerator DestinationHosePickupUsesGrabInputAndAnimationEvents()
    {
        EditorSceneManager.OpenScene(Scene);
        yield return new EnterPlayMode();
        HoseCheck("ResetKeyboard"); yield return Wait(.3f);
        var p1 = GameObject.Find("Player1"); var p2 = GameObject.Find("Player2");
        var front = GameObject.Find("HoseNozzle");
        var hose = C(front, "TA.AdaptiveAnimation.CooperativeFireHose");
        Position(p1, new Vector3(front.transform.position.x, .35f, front.transform.position.z - .45f), Quaternion.identity);
        Position(p2, p1.transform.position - Vector3.forward * 1.05f, Quaternion.identity);
        yield return Wait(.4f);
        Keys("F"); yield return Wait(.08f); Keys();
        var a = C(p1, "HPlayer.InteractionController");
        Assert.IsTrue((bool)P(a, "IsPickingUp"), "Grab must start pickup animation");
        for (float until = Time.time + 4f; (bool)P(a, "IsPickingUp") && Time.time < until;) yield return null;
        Assert.AreSame(C(front, "TA.AdaptiveAnimation.CooperativeHoseGrip"), P(a, "HeldObject"));
        var b = C(p2, "HPlayer.InteractionController");
        var rear = GameObject.Find("Hose_RearGrip");
        var rearGrip = C(rear, "TA.AdaptiveAnimation.CooperativeHoseGrip");
        var rearTrigger = rear.GetComponent<BoxCollider>();
        var handTrigger = (BoxCollider)F(b, "handTrigger");
        // The unheld grip follows the cable, so aim the grab trigger at its live position.
        yield return Wait(.4f);
        for (float until = Time.time + .8f; !ReferenceEquals(P(b, "SelectedObject"), rearGrip) && Time.time < until;)
        {
            Vector3 handOffset = handTrigger.transform.TransformPoint(handTrigger.center) - p2.transform.position;
            Vector3 stance = rearTrigger.bounds.center - handOffset;
            stance.y = .35f;
            Position(p2, stance, Quaternion.identity);
            yield return Wait(.08f);
        }
        Assert.AreSame(rearGrip, P(b, "SelectedObject"), "Player 2 must be within grab reach of the live rear grip");
        Keys("RightCtrl"); yield return Wait(.08f); Keys();
        Assert.IsTrue((bool)P(b, "IsPickingUp"), "Player 2 Grab must start the rear pickup animation");
        for (float until = Time.time + 4f; (bool)P(b, "IsPickingUp") && Time.time < until;) yield return null;
        Assert.AreSame(rearGrip, P(b, "HeldObject"));
        yield return Wait(.8f);
        Assert.IsTrue((bool)P(hose, "ReadyToSpray"));
    }

    static Transform VisibleBone(GameObject player, string name)
    {
        var visual = (GameObject)F(C(player, "TA.AdaptiveAnimation.CharacterModelSlot"), "generatedVisuals");
        return visual.GetComponentsInChildren<Transform>().First(t => t.name == name);
    }

    static void VisiblePropContacts(GameObject player)
    {
        // The same analytical visible-hand checks used for hoses apply to prop IK targets.
        HoseCheck("CheckMovingContacts", player);
    }

    static float BackwardChestRotation(GameObject player, Quaternion neutral)
    {
        var delta = VisibleBone(player, "spine.003").rotation * Quaternion.Inverse(neutral);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        return Vector3.Dot(axis * angle, Vector3.Cross(Vector3.up, -player.transform.forward));
    }

    static Vector3 ClosestTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float sum = va + vb + vc;
        return Mathf.Abs(sum) < 1e-12f ? a : a + ab * (vb / sum) + ac * (vc / sum);
    }

    static void CheckPropSurface(GameObject player, GameObject item, List<string> issues)
    {
        foreach (string side in new[] { "L", "R" })
        {
            Vector3 hand = VisibleBone(player, "hand." + side).position;
            float distance = float.PositiveInfinity;
            foreach (var filter in item.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!renderer || !renderer.enabled || !filter.sharedMesh) continue;
                var vertices = filter.sharedMesh.vertices; var triangles = filter.sharedMesh.triangles;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = filter.transform.TransformPoint(vertices[i]);
                for (int i = 0; i < triangles.Length; i += 3)
                    distance = Mathf.Min(distance, Vector3.Distance(hand, ClosestTriangle(hand,
                        vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]])));
            }
            string sample = item.name + " " + player.name + " " + side + " hand-to-visible-surface=" + distance;
            Debug.Log(sample);
            if (distance > .025f) issues.Add(sample);
        }
    }

    [UnityTest]
    public IEnumerator DestinationPropsPickupVisibleContactsAndBodyLean()
    {
        EditorSceneManager.OpenScene(Scene);
        yield return new EnterPlayMode();
        HoseCheck("ResetKeyboard");
        var adapter = C(GameObject.Find("FR_Level_1"), "TA.AdaptiveAnimation.AdaptiveSceneProps");
        for (float until = Time.time + 3f; !(bool)P(adapter, "IsReady") && Time.time < until;) yield return null;
        Assert.IsTrue((bool)P(adapter, "IsReady"));
        var p1 = GameObject.Find("Player1"); var p2 = GameObject.Find("Player2");
        var a = C(p1, "HPlayer.InteractionController"); var b = C(p2, "HPlayer.InteractionController");
        var surfaceIssues = new List<string>();
        var computer = GameObject.Find("Carry_Props_Computer_01");
        var item = C(computer, "HInteractions.Liftable");
        Assert.IsTrue((bool)P(C(computer, "TA.AdaptiveAnimation.ScenePropGripProvider"), "HasSurfaceGrips"));
        var load = C(computer, "TA.AdaptiveAnimation.CarryAnimationLoad");
        float originalWeight = (float)P(load, "WeightKg");
        Position(p1, new Vector3(30f, .35f, 28f), Quaternion.identity);
        Position(p2, new Vector3(34f, .35f, 28f), Quaternion.identity);
        Position(computer, new Vector3(30f, .75f, 28.65f), Quaternion.identity);
        yield return Wait(.3f);
        Call(a, "BeginPickup", item);
        Assert.IsTrue((bool)P(a, "IsPickingUp"));
        for (float until = Time.time + 4f; (bool)P(a, "IsPickingUp") && Time.time < until;) yield return null;
        Assert.AreSame(item, P(a, "HeldObject"), "Pickup_Grab must attach the level prop");
        F(load, "weightKg"); // Verify the authored load field before changing this test instance.
        load.GetType().GetField("weightKg", Flags).SetValue(load, 0f);
        yield return Wait(1.5f);
        Quaternion neutral = VisibleBone(p1, "spine.003").rotation;
        load.GetType().GetField("weightKg", Flags).SetValue(load, 50f);
        yield return Wait(1.5f);
        var lean = p1.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectBodyLeanController"));
        Assert.That((float)F(lean, "currentLoadKg"), Is.EqualTo(50f).Within(.01f));
        Assert.That((float)F(lean, "currentStylizedAngle"), Is.EqualTo(24f).Within(.6f));
        Assert.Greater((float)F(lean, "currentHeadLookUp"), 3f);
        float backward = BackwardChestRotation(p1, neutral);
        Debug.Log("GAMELOOP PROPS computer visible backward lean=" + backward);
        Assert.Greater(backward, 15f, "Visible replacement chest must lean backward under load");
        VisiblePropContacts(p1);
        CheckPropSurface(p1, computer, surfaceIssues);
        Keys("D"); yield return Wait(.5f); Keys();
        VisiblePropContacts(p1);
        CheckPropSurface(p1, computer, surfaceIssues);
        CooperativeHoseTests.Capture("Temp/Gameloop-Computer-Carry.png", p1.transform.position + Vector3.up * .4f);
        load.GetType().GetField("weightKg", Flags).SetValue(load, originalWeight);
        Call(a, "ReleaseHeldObject"); yield return Wait(1f);
        Assert.Less(Mathf.Abs((float)F(lean, "currentStylizedAngle")), .1f);

        var table = GameObject.Find("Carry_Props_Table_01");
        var team = C(table, "TA.AdaptiveAnimation.CooperativeCarryObject");
        Assert.IsTrue((bool)P(C(table, "TA.AdaptiveAnimation.ScenePropGripProvider"), "HasSurfaceGrips"));
        var teamLoad = C(table, "TA.AdaptiveAnimation.CarryAnimationLoad");
        Position(table, new Vector3(30f, .75f, 32f), Quaternion.identity);
        var role = T("TA.AdaptiveAnimation.CarryRole");
        Position(p1, (Vector3)Call(team, "GetStance", Enum.Parse(role, "Front"), .35f), Quaternion.Euler(0f, 180f, 0f));
        Position(p2, (Vector3)Call(team, "GetStance", Enum.Parse(role, "Back"), .35f), Quaternion.identity);
        Call(a, "PickUpObject", team); Call(b, "PickUpObject", team);
        for (float until = Time.time + 4f; P(team, "State").ToString() != "Carrying" && Time.time < until;) yield return null;
        Assert.AreEqual("Carrying", P(team, "State").ToString());
        teamLoad.GetType().GetField("weightKg", Flags).SetValue(teamLoad, 0f);
        yield return Wait(1.5f);
        Quaternion neutral1 = VisibleBone(p1, "spine.003").rotation;
        Quaternion neutral2 = VisibleBone(p2, "spine.003").rotation;
        teamLoad.GetType().GetField("weightKg", Flags).SetValue(teamLoad, 60f);
        yield return Wait(1.5f);
        foreach (var player in new[] { p1, p2 })
        {
            var teamLean = player.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectBodyLeanController"));
            Assert.That((float)F(teamLean, "currentLoadKg"), Is.EqualTo(30f).Within(.1f));
            Assert.That((float)F(teamLean, "currentStylizedAngle"), Is.EqualTo(16f).Within(.6f));
            Assert.Greater(BackwardChestRotation(player, player == p1 ? neutral1 : neutral2), 10f);
            VisiblePropContacts(player);
            CheckPropSurface(player, table, surfaceIssues);
        }
        Vector3 before = table.transform.position;
        Keys("D", "RightArrow"); yield return Wait(.8f); Keys();
        Assert.Greater(Vector3.Distance(before, table.transform.position), .1f, "Team input moves actual furniture");
        VisiblePropContacts(p1); VisiblePropContacts(p2);
        CheckPropSurface(p1, table, surfaceIssues); CheckPropSurface(p2, table, surfaceIssues);
        CooperativeHoseTests.Capture("Temp/Gameloop-Table-Carry.png", (p1.transform.position + p2.transform.position) * .5f + Vector3.up * .4f);
        Call(a, "ReleaseHeldObject"); yield return Wait(1f);
        Assert.IsNull(P(b, "HeldObject"));
        Assert.IsFalse(table.GetComponent<Rigidbody>().isKinematic);
        foreach (var player in new[] { p1, p2 })
            Assert.Less(Mathf.Abs((float)F(player.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectBodyLeanController")), "currentStylizedAngle")), .1f);
        Assert.IsEmpty(surfaceIssues, "Contact targets must also match the visible prop surface");
    }
}
