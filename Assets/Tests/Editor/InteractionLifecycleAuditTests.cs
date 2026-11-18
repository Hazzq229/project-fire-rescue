using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public class InteractionLifecycleAuditTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static Component C(GameObject go, string type) => go.GetComponent(T(type));
    static object P(object obj, string name) => obj.GetType().GetProperty(name).GetValue(obj);
    static object F(object obj, string name) => obj.GetType().GetField(name, Flags).GetValue(obj);
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethods(Flags)
        .First(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v)).Invoke(obj, args);
    static void ResetKeyboard() => typeof(CooperativeHoseTests)
        .GetMethod("ResetKeyboard", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    static IEnumerator Wait(float seconds)
    {
        for (float until = Time.time + seconds; Time.time < until;) yield return null;
    }
    static void Position(GameObject go, Vector3 position, Quaternion rotation)
    {
        go.transform.SetPositionAndRotation(position, rotation);
        var body = go.GetComponent<Rigidbody>();
        body.position = position;
        body.rotation = rotation;
        if (!body.isKinematic) body.velocity = Vector3.zero;
        Physics.SyncTransforms();
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying)
        {
            ResetKeyboard();
            yield return new ExitPlayMode();
        }
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
    }

    [UnityTest]
    public IEnumerator LampPickedUpAfterReservationDoesNotToggleRemotelyOrKeepActorLocked()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity");
        yield return new EnterPlayMode();
        yield return CheckLampReservationPickup();
    }

    static IEnumerator CheckLampReservationPickup()
    {
        ResetKeyboard();
        yield return Wait(.5f);
        var p1 = GameObject.Find("Player1");
        var p2 = GameObject.Find("Player2");
        var actor = C(p1, "HPlayer.InteractionController");
        var carrier = C(p2, "HPlayer.InteractionController");
        var controller = C(p1, "HPlayer.DoorInteractionController");
        var lamp = C(GameObject.Find("Adaptive_Lamp"), "HGame.Objects.LampInteractable");
        var point = (Transform)Call(lamp, "GetInteractionPoint", 0);
        var liftable = lamp.GetComponentInParent(T("HInteractions.Liftable"));
        Assert.IsNotNull(liftable, "Use the destination lamp's actual adapted carry root");
        Position(p2, new Vector3(30f, .35f, 30f), Quaternion.identity);
        Position(p1, point.position + new Vector3(-.6f, 0f, -.2f), point.rotation);
        yield return Wait(.3f);
        bool before = (bool)P(lamp, "IsOn");
        Call(controller, "TryInteract");
        Assert.AreSame(lamp, P(controller, "ActiveTarget"));
        Assert.IsTrue((bool)P(actor, "ExternalInteractionLocked"));
        for (float until = Time.time + 3f; !(bool)F(controller, "entered") && Time.time < until;) yield return null;
        Assert.IsTrue((bool)F(controller, "entered"), "The real switch animation must start before the competing pickup");
        Assert.IsFalse((bool)F(controller, "contactReceived"), "Pickup must happen before the animation's contact event");
        Assert.AreEqual(before, P(lamp, "IsOn"));

        // Invoke the same ownership path used by Pickup_Grab. The race under test
        // starts after reservation, rather than the already-covered held-lamp rejection.
        Position(p2, point.position + point.right * 1.2f - point.forward * .4f, point.rotation);
        Call(carrier, "PickUpObject", liftable);
        Assert.AreSame(liftable, P(carrier, "HeldObject"));
        Assert.IsTrue((bool)P(liftable, "IsLifted"));
        bool toggledWhileCarried = false;
        for (float until = Time.time + 10f; (bool)P(controller, "IsInteractingWithObject") && Time.time < until;)
        {
            toggledWhileCarried |= before != (bool)P(lamp, "IsOn");
            yield return null;
        }
        toggledWhileCarried |= before != (bool)P(lamp, "IsOn");
        Assert.IsFalse(toggledWhileCarried, "A stale switch animation must not toggle the partner's carried lamp");
        Assert.AreEqual(before, P(lamp, "IsOn"));
        Assert.IsFalse((bool)P(controller, "IsInteractingWithObject"), "The invalidated action must finish or cancel");
        Assert.IsNull(P(controller, "ActiveTarget"));
        Assert.IsFalse((bool)P(actor, "ExternalInteractionLocked"), "Reservation cancellation must unlock interactions");
        Assert.IsFalse((bool)F(C(p1, "ThirdPersonPlayerController"), "_movementLocked"), "Reservation cancellation must unlock movement");
        Assert.AreSame(liftable, P(carrier, "HeldObject"), "Canceling the switch must preserve the partner's carry ownership");
        Call(carrier, "ReleaseHeldObject");
    }

    [UnityTest]
    public IEnumerator HoseNormalDropRestoresFreeCableConstraints()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        yield return CheckHoseRelease("drop");
    }

    [UnityTest]
    public IEnumerator HoseDisableRestoresFreeCableConstraints()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        yield return CheckHoseRelease("disable");
    }

    [UnityTest]
    public IEnumerator HoseBurstRestoresFreeCableConstraints()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        yield return CheckHoseRelease("burst");
    }

    sealed class BodyFlags
    {
        public Rigidbody body;
        public bool kinematic, gravity;
    }

    static IEnumerator CheckHoseRelease(string release)
    {
        ResetKeyboard();
        yield return Wait(.3f);
        var p1 = GameObject.Find("Player1");
        var p2 = GameObject.Find("Player2");
        var a = C(p1, "HPlayer.InteractionController");
        var b = C(p2, "HPlayer.InteractionController");
        var front = GameObject.Find("HoseNozzle");
        var rear = GameObject.Find("Hose_RearGrip");
        var hose = C(front, "TA.AdaptiveAnimation.CooperativeFireHose");
        var cable = (Component)F(hose, "cable");
        var bodies = cable.GetComponentsInChildren<Rigidbody>().Select(body => new BodyFlags
        {
            body = body, kinematic = body.isKinematic, gravity = body.useGravity
        }).ToArray();
        var joints = cable.GetComponentsInChildren<SpringJoint>()
            .Where(j => j.connectedBody && j.connectedBody.transform.IsChildOf(cable.transform)).ToArray();
        Assert.Greater(joints.Length, 3, "Exercise the actual free cable and its supported spans");
        float originalLength = (float)P(cable, "CableLength");
        foreach (var joint in joints)
        {
            Assert.That(joint.minDistance, Is.EqualTo(originalLength / joints.Length).Within(.0001f));
            Assert.That(joint.maxDistance, Is.EqualTo(originalLength / joints.Length).Within(.0001f));
        }
        Position(p1, new Vector3(front.transform.position.x, .68f, front.transform.position.z - .45f), Quaternion.identity);
        Call(a, "PickUpObject", C(front, "TA.AdaptiveAnimation.CooperativeHoseGrip"));
        Position(p2, (Vector3)Call(hose, "RearStance"), Quaternion.identity);
        Call(b, "PickUpObject", C(rear, "TA.AdaptiveAnimation.CooperativeHoseGrip"));
        Assert.IsTrue((bool)P(hose, "ReadyToSpray"));
        yield return Wait(.2f);
        Assert.AreEqual(3, ((IEnumerable)F(hose, "supportedPoints")).Cast<object>().Count());

        // Change the selected reel budget while supported. Stopping immediately
        // keeps automatic FixedUpdate reeling from changing the expected total.
        Call(hose, "ToggleReel", b, 1);
        Call(hose, "StepReel", 2f);
        Call(hose, "ToggleReel", b, 1);
        float selectedLength = (float)P(cable, "CableLength");
        Assert.Greater(selectedLength, originalLength, "The release must preserve a player-selected reel length");
        Assert.That(joints.Sum(j => j.maxDistance), Is.EqualTo(selectedLength).Within(.01f));
        Assert.IsTrue(joints.Any(j => Mathf.Abs(j.maxDistance - selectedLength / joints.Length) > .005f),
            "The carried fixture must actually use nonuniform support-span constraints");

        if (release == "drop") Call(a, "ReleaseHeldObject");
        else if (release == "disable") ((Behaviour)hose).enabled = false;
        else
        {
            int beforeBurst = (int)P(hose, "BurstCount");
            for (int i = 0; i < 80 && (int)P(hose, "BurstCount") == beforeBurst; i++)
                Call(hose, "StepPressure", true, .1f);
            Assert.AreEqual(beforeBurst + 1, P(hose, "BurstCount"), "Exercise pressure's actual burst release path");
        }
        Assert.IsNull(P(a, "HeldObject"));
        Assert.IsNull(P(b, "HeldObject"));
        Assert.IsNull(P(C(p1, "ThirdPersonPlayerController"), "CooperativeHose"));
        Assert.IsNull(P(C(p2, "ThirdPersonPlayerController"), "CooperativeHose"));
        Assert.AreEqual(0, ((IEnumerable)F(hose, "supportedPoints")).Cast<object>().Count());
        Assert.That((float)P(cable, "CableLength"), Is.EqualTo(selectedLength).Within(.0001f), "Release must retain the selected reel budget");
        foreach (var saved in bodies)
        {
            Assert.AreEqual(saved.kinematic, saved.body.isKinematic, release + ": " + saved.body.name + " kinematic state");
            Assert.AreEqual(saved.gravity, saved.body.useGravity, release + ": " + saved.body.name + " gravity state");
        }
        foreach (var joint in joints)
        {
            Assert.That(joint.minDistance, Is.EqualTo(selectedLength / joints.Length).Within(.0001f), release + ": " + joint.name + " free minDistance");
            Assert.That(joint.maxDistance, Is.EqualTo(selectedLength / joints.Length).Within(.0001f), release + ": " + joint.name + " free maxDistance");
        }
        Assert.That(joints.Sum(j => j.maxDistance), Is.EqualTo(selectedLength).Within(.01f));
    }
}
