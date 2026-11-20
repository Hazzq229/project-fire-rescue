using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class SurvivorFlowTests
{
    private const string Scene = "Assets/Scenes/Gameloop_IJUN.unity";
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Vector3 TestFloor = new Vector3(100f, 20f, 100f);
    private static NavMeshData testNavigation;
    private static NavMeshDataInstance testNavigationInstance;

    private static Type RuntimeType(string name)
    {
        return AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name))
            .First(type => type != null);
    }

    private static FieldInfo Field(Component component, string name)
    {
        return component.GetType().GetField(name, InstanceFields);
    }

    private static object Call(Component component, string name, params object[] args)
    {
        return component.GetType().GetMethod(name, InstanceFields).Invoke(component, args);
    }

    private static object Property(Component component, string name)
    {
        return component.GetType().GetProperty(name).GetValue(component);
    }

    private static string State(Component victim) => Field(victim, "currentState").GetValue(victim).ToString();
    private static bool IsSafe(Component victim) => (bool)Field(victim, "isSafe").GetValue(victim);

    private static IEnumerator WaitFor(Func<bool> ready, float timeout, string message)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.IsTrue(ready(), message);
    }

    private static IEnumerator Wait(float duration)
    {
        float until = Time.time + duration;
        while (Time.time < until) yield return null;
    }

    private static void Position(Component subject, Vector3 point, Quaternion rotation)
    {
        var body = subject.GetComponent<Rigidbody>();
        subject.transform.SetPositionAndRotation(point, rotation);
        body.position = point;
        body.rotation = rotation;
        if (!body.isKinematic)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
    }

    private static bool HasPose(Animator animator, string state)
    {
        return !animator.IsInTransition(0) && animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer." + state);
    }

    private static void DisableAmbientFire()
    {
        foreach (Behaviour spawner in Object.FindObjectsOfType(RuntimeType("FireSpawner"))) spawner.enabled = false;
        foreach (Behaviour ambientFire in Object.FindObjectsOfType(RuntimeType("Fire")))
        {
            ambientFire.enabled = false;
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(ambientFire);
        }
    }

    private static Component CreateFire(Vector3 position, Vector3 size, float ignitionRate)
    {
        var fireObject = new GameObject("Survivor exposure test fire");
        fireObject.transform.position = position;
        var heat = fireObject.AddComponent<BoxCollider>();
        heat.isTrigger = true;
        heat.size = size;
        var fire = fireObject.AddComponent(RuntimeType("Fire"));
        Field(fire, "ignitionRate").SetValue(fire, ignitionRate);
        Physics.SyncTransforms();
        return fire;
    }

    private static IEnumerator PickUp(Component actor, Component victim, Component liftable)
    {
        Vector3 point = victim.transform.position - Vector3.right * .8f;
        var capsule = actor.GetComponent<CapsuleCollider>();
        point.y = TestFloor.y + actor.transform.position.y - capsule.bounds.min.y + .03f;
        Position(actor, point, Quaternion.LookRotation(Vector3.right));
        Call(actor, "BeginPickup", liftable);
        Assert.IsTrue((bool)Property(actor, "IsPickingUp"), "The player must enter its normal pickup animation");
        yield return WaitFor(() => ReferenceEquals(liftable, Property(actor, "HeldObject")) &&
                                   !(bool)Property(actor, "IsPickingUp"), 6f,
            "The existing pickup animation event must attach the survivor");
    }

    private static IEnumerator VerifyObstacleDetour(Component victim, NavMeshAgent agent)
    {
        Vector3 start = victim.transform.position;
        start.y = TestFloor.y;
        Vector3 center = start - Vector3.right * .9f;
        Vector3 beyond = start - Vector3.right * 2f;
        Assert.IsFalse(NavMesh.Raycast(start, beyond, out _, NavMesh.AllAreas),
            "The route must start clear before furniture is moved into it");

        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.name = "Moved furniture navigation test";
        blocker.transform.localScale = new Vector3(.34f, 1.5f, 1.1f);
        blocker.transform.position = center + Vector3.up * .75f;
        var obstacle = blocker.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = Vector3.one;
        obstacle.carveOnlyStationary = false;
        obstacle.carving = true;
        Physics.SyncTransforms();

        yield return WaitFor(() => NavMesh.Raycast(start, beyond, out _, NavMesh.AllAreas),
            3f, "Moved furniture must carve its occupied space into the active NavMesh");
        yield return WaitFor(() =>
        {
            Vector3 point = victim.transform.position;
            return agent.isOnNavMesh && point.x < center.x - .18f &&
                   Mathf.Abs(point.z - center.z) > .55f;
        }, 7f, "The fleeing NPC must take another route around the solid furniture and keep moving");
        Object.Destroy(blocker);
    }

    [UnityTest]
    public IEnumerator SquirrelWalksFleesCollapsesAndRecoversAfterSafeDelivery()
    {
        EditorSceneManager.OpenScene(Scene);
        var victimType = RuntimeType("VictimAI");
        var victimObject = GameObject.Find("Victim");
        Assert.IsNotNull(victimObject, "The gameplay scene must contain its original survivor before entering Play Mode");
        var victim = victimObject.GetComponent(victimType);
        Assert.IsNotNull(victim);
        Assert.IsTrue(((Behaviour)victim).enabled);
        var configuredAnimator = (Animator)Field(victim, "animator").GetValue(victim);
        Assert.IsNotNull(configuredAnimator, "The saved survivor must have the Tupai Animator assigned");
        CollectionAssert.AreEquivalent(new[]
        {
            "Idle Waspada", "Diam ke Jalan Santai", "Lari Ketakutan", "Sesak lalu Pingsan", "Sadar Kembali"
        }, configuredAnimator.runtimeAnimatorController.animationClips.Select(clip => clip.name));

        // Isolate the authored components from randomized fires and level furniture.
        foreach (var other in Object.FindObjectsOfType(victimType).Cast<Component>())
        {
            if (other == victim) continue;
            other.gameObject.SetActive(false);
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(other.gameObject);
        }
        DisableAmbientFire();
        victim.transform.position = TestFloor + Vector3.up;
        Field(victim, "radiusX").SetValue(victim, 9f);
        Field(victim, "radiusZ").SetValue(victim, 9f);
        Field(victim, "idleDuration").SetValue(victim, 4f);
        Field(victim, "walkDuration").SetValue(victim, 1.2f);
        Field(victim, "maxHealth").SetValue(victim, 40f);
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(victim);
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(victim.transform);
        var zone = (Component)Object.FindObjectOfType(RuntimeType("SafeZoneTrigger"));
        Assert.IsNotNull(zone, "Rescue must use the safe zone already in the scene");
        zone.transform.position = TestFloor + Vector3.forward * 20f + Vector3.up * .168f;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Survivor flow test floor";
        floor.transform.position = TestFloor + Vector3.forward * 8f - Vector3.up * .25f;
        floor.transform.localScale = new Vector3(62f, .5f, 62f);
        yield return new EnterPlayMode();
        CreateTestNavigation();
        yield return ExerciseSurvivorFlow();
    }

    private static void CreateTestNavigation()
    {
        var floor = GameObject.Find("Survivor flow test floor");
        Assert.IsNotNull(floor);
        var settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = .36f;
        settings.agentHeight = 1.08f;
        settings.agentClimb = .23f;
        var sources = new System.Collections.Generic.List<NavMeshBuildSource>
        {
            new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.Translate(floor.transform.position),
                size = floor.transform.localScale
            }
        };
        testNavigation = NavMeshBuilder.BuildNavMeshData(settings, sources,
            new Bounds(floor.transform.position, new Vector3(62f, 4f, 62f)), Vector3.zero,
            Quaternion.identity);
        Assert.IsNotNull(testNavigation, "The isolated floor must bake into a NavMesh");
        testNavigationInstance = NavMesh.AddNavMeshData(testNavigation);
        Assert.IsTrue(testNavigationInstance.valid);
        Assert.IsTrue(NavMesh.SamplePosition(TestFloor, out _, .1f, NavMesh.AllAreas));
    }

    // Build the closures after Unity reloads the domain on Play Mode entry.
    private static IEnumerator ExerciseSurvivorFlow()
    {
        var victim = GameObject.Find("Victim").GetComponent(RuntimeType("VictimAI"));
        var zone = (Component)Object.FindObjectOfType(RuntimeType("SafeZoneTrigger"));
        var animator = (Animator)Field(victim, "animator").GetValue(victim);
        var agent = victim.GetComponent<NavMeshAgent>();
        Assert.IsNotNull(agent, "The saved Tupai prefab must include its navigation agent");
        var liftable = victim.GetComponent(RuntimeType("HInteractions.Liftable"));
        var actor = GameObject.Find("Player1").GetComponent(RuntimeType("HPlayer.InteractionController"));
        var manager = (Component)Object.FindObjectOfType(RuntimeType("GameManager"));
        // Exercise the rescue objective independently of any round fire spread.
        Field(manager, "requireAllFiresExtinguishedToWin").SetValue(manager, false);
        yield return WaitFor(() => ((IList)Field(manager, "activeVictims").GetValue(manager)).Contains(victim),
            3f, "The round objective must discover the saved survivor");
        Assert.AreEqual("Playing", Property(manager, "CurrentState").ToString());
        yield return WaitFor(() => agent.isActiveAndEnabled && agent.isOnNavMesh, 3f,
            "The survivor must bind to the walkable surface");
        yield return WaitFor(() => State(victim) == "Calm" && HasPose(animator, "Idle"), 3f,
            "The survivor must idle without fire nearby");
        Field(victim, "idleDuration").SetValue(victim, .65f);
        Field(victim, "calmUntil").SetValue(victim, Time.time + .15f);
        Vector3 idle = victim.transform.position;
        yield return WaitFor(() => State(victim) == "Walking" && HasPose(animator, "Walk") &&
                                   Vector3.ProjectOnPlane(victim.transform.position - idle, Vector3.up).magnitude > .15f, 4f,
            "The survivor must use its walk clip and move when the area is clear");

        float panicRadius = (float)Field(victim, "panicRadius").GetValue(victim);
        var fire = CreateFire(victim.transform.position + Vector3.right * (panicRadius * .52f), Vector3.one * .35f, 40f);
        var heat = fire.GetComponent<BoxCollider>();
        Vector3 beforePanic = victim.transform.position;
        yield return WaitFor(() => State(victim) == "Panic" && HasPose(animator, "Panic") &&
                                   victim.transform.position.x < beforePanic.x - .25f, 3f,
            "An active nearby fire must trigger the panic clip and flight away from it");
        Assert.That((float)Field(victim, "currentHealth").GetValue(victim), Is.EqualTo(40f).Within(.001f),
            "Fire outside the heat range should scare the survivor without causing damage");
        yield return VerifyObstacleDetour(victim, agent);

        // Widen the fire trigger so the fleeing survivor receives continuous exposure.
        heat.size = new Vector3(32f, 5f, 32f);
        Physics.SyncTransforms();
        yield return Wait(.2f);
        Assert.AreNotEqual("Downed", State(victim), "A short exposure must not drop the survivor immediately");
        Assert.Less((float)Field(victim, "currentHealth").GetValue(victim), 40f);
        yield return WaitFor(() => State(victim) == "Downed" && HasPose(animator, "Collapse") &&
                                   animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f, 7f,
            "Sustained fire contact must play the complete collapse animation");
        Assert.IsFalse(agent.enabled, "The agent must suspend while the survivor is down");
        Assert.IsTrue((bool)Call(fire, "TryExtinguish", 2f));
        Assert.IsFalse(IsSafe(victim));

        yield return PickUp(actor, victim, liftable);
        Assert.AreEqual("Downed", State(victim), "Carrying must keep the collapsed pose");
        Assert.IsFalse(agent.enabled, "Navigation must not pull the carried survivor off the player");
        Call(actor, "ReleaseHeldObject");
        yield return Wait(.35f);
        Assert.AreEqual("Downed", State(victim), "Release outside the safe zone must not revive the survivor");
        Assert.IsFalse(IsSafe(victim));

        yield return PickUp(actor, victim, liftable);
        Vector3 route = zone.transform.position - Vector3.right * .8f - actor.transform.position;
        route.y = 0f;
        Position(actor, actor.transform.position + route, actor.transform.rotation);
        Position(victim, zone.transform.position + Vector3.up * .7f, victim.transform.rotation);
        yield return Wait(.12f);
        Assert.IsFalse(IsSafe(victim), "A held survivor has not yet been delivered");
        Assert.AreEqual("Downed", State(victim));
        Call(actor, "BeginPlaceDown");
        Assert.IsTrue((bool)Property(actor, "IsPlacingDown"), "The safe zone floor must support normal placement");
        yield return WaitFor(() => Property(actor, "HeldObject") == null && State(victim) == "Recovering" &&
                                   IsSafe(victim) && HasPose(animator, "Recover"),
            6f, "Placing the survivor in the real safe zone must start its recovery animation");
        yield return WaitFor(() => State(victim) == "Rescued" && HasPose(animator, "Idle"), 6f,
            "Recovery must finish and leave the saved survivor upright and idle");
        Assert.IsFalse(agent.enabled, "The rescued survivor must stay in the safe zone");
        Assert.IsFalse(((Behaviour)liftable).enabled, "A rescued survivor must no longer be offered for pickup");
        yield return WaitFor(() => Property(manager, "CurrentState").ToString() == "Victory", 3f,
            "Completing the rescue objective must finish a rescue-only round");
        var objectives = ((IList)Field(manager, "activeVictims").GetValue(manager)).Cast<Component>().ToArray();
        var counter = (Component)Field(manager, "victimText").GetValue(manager);
        Assert.AreEqual(objectives.Length, objectives.Count(IsSafe));
        Assert.AreEqual($"{objectives.Length} / {objectives.Length}", Property(counter, "text"));
    }

    [UnityTest]
    public IEnumerator SustainedContactAndNearbyHeatCanKillBothSurvivorModels()
    {
        EditorSceneManager.OpenScene(Scene);
        DisableAmbientFire();
        yield return new EnterPlayMode();
        yield return ExerciseFatalHeat();
    }

    private static IEnumerator ExerciseFatalHeat()
    {
        var victimType = RuntimeType("VictimAI");
        var tupai = GameObject.Find("Victim").GetComponent(victimType);
        var badak = GameObject.Find("Badak - Survivor").GetComponent(victimType);
        var victims = new[] { tupai, badak };
        for (int i = 0; i < victims.Length; i++)
        {
            // Keep each authored model in its test exposure while using its real collider.
            victims[i].GetComponent<Rigidbody>().isKinematic = true;
            Position(victims[i], TestFloor + Vector3.up + Vector3.right * (8f * i), Quaternion.identity);
        }

        const float fireWidth = .4f;
        const float openGap = .2f;
        var tupaiShape = tupai.GetComponent<CapsuleCollider>();
        var badakShape = badak.GetComponent<CapsuleCollider>();
        var contact = CreateFire(tupaiShape.bounds.center, Vector3.one * fireWidth, 20f);
        var nearCenter = badakShape.bounds.center + Vector3.right * (badakShape.bounds.extents.x + fireWidth * .5f + openGap);
        var nearby = CreateFire(nearCenter, Vector3.one * fireWidth, 20f);
        Assert.IsTrue(tupaiShape.bounds.Intersects(contact.GetComponent<Collider>().bounds));
        Assert.IsFalse(badakShape.bounds.Intersects(nearby.GetComponent<Collider>().bounds),
            "The Badak fixture must test heat beyond the actual fire trigger");

        yield return Wait(.2f);
        foreach (var victim in victims)
        {
            float health = (float)Field(victim, "currentHealth").GetValue(victim);
            Assert.Less(health, (float)Field(victim, "maxHealth").GetValue(victim), victim.name);
            Assert.AreNotEqual("Dead", State(victim), "Heat must leave time to rescue " + victim.name);
        }

        yield return WaitFor(() => victims.All(victim => State(victim) == "Downed"), 8f,
            "Both types of sustained exposure must knock down their survivor before causing death");
        yield return WaitFor(() => victims.All(victim => State(victim) == "Dead"), 14f,
            "Staying in burning or near-fire heat must kill the downed survivor");

        Assert.IsTrue((bool)Call(contact, "TryExtinguish", 2f));
        Assert.IsTrue((bool)Call(nearby, "TryExtinguish", 2f));
        foreach (var victim in victims)
        {
            var animator = (Animator)Field(victim, "animator").GetValue(victim);
            Assert.IsTrue(HasPose(animator, "Collapse"), victim.name);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThanOrEqualTo(1f), victim.name);
            Assert.IsFalse(victim.GetComponent<NavMeshAgent>().enabled, victim.name);
            Assert.IsFalse(((Behaviour)victim.GetComponent(RuntimeType("HInteractions.Liftable"))).enabled, victim.name);
            Assert.IsFalse(IsSafe(victim), "The rescue count must exclude " + victim.name);
        }
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (testNavigationInstance.valid) testNavigationInstance.Remove();
        if (testNavigation) Object.DestroyImmediate(testNavigation);
        if (Application.isPlaying) yield return new ExitPlayMode();
        EditorSceneManager.OpenScene(Scene);
    }
}
