using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ObjectInteractionTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static object Call(object obj, string name, params object[] args)
    {
        var method = obj.GetType().GetMethods(Flags).First(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(b => b));
        return method.Invoke(obj, args);
    }
    static object Prop(object obj, string name) => obj.GetType().GetProperty(name).GetValue(obj);
    static Component Controller(GameObject player) => player.GetComponent(T("HPlayer.DoorInteractionController"));
    static void AtPoint(GameObject player, Transform point)
    {
        var rb = player.GetComponent<Rigidbody>();
        rb.position = new Vector3(point.position.x, .68f, point.position.z);
        rb.rotation = point.rotation;
        rb.velocity = Vector3.zero;
        Physics.SyncTransforms();
    }
    static IEnumerator WaitIdle(object controller)
    {
        for (float until = Time.time + 9f; (bool)Prop(controller, "IsInteractingWithObject") && Time.time < until;) yield return null;
        Assert.IsFalse((bool)Prop(controller, "IsInteractingWithObject"), "Interaction must finish and release controls");
    }

    [UnityTest]
    public IEnumerator LampContactOwnershipAndCancellation()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        var player = GameObject.Find("Player1");
        var second = GameObject.Find("Player2");
        var controller = Controller(player);
        var other = Controller(second);
        var interaction = player.GetComponent(T("HPlayer.InteractionController"));
        var lamp = GameObject.Find("Props_Lamp_Interactive").GetComponent(T("HGame.Objects.LampInteractable"));
        var point = (Transform)Call(lamp, "GetInteractionPoint", 0);
        var light = ((Component)lamp).GetComponentInChildren<Light>();
        Assert.IsTrue((bool)Prop(lamp, "IsOn"));
        player.GetComponent<Rigidbody>().position = new Vector3(10f, .68f, -8f);
        Call(controller, "TryInteract");
        Assert.IsFalse((bool)Prop(controller, "IsInteractingWithObject"), "Outside range must not activate");
        AtPoint(player, point);
        player.GetComponent<Rigidbody>().position += Vector3.left * .2f;
        Physics.SyncTransforms();
        for (float until = Time.time + .4f; Time.time < until;) yield return null;
        var box = GameObject.Find("Box_Small (2)").GetComponent(T("HInteractions.Liftable"));
        Call(interaction, "PickUpObject", box);
        Call(controller, "TryInteract");
        Assert.IsFalse((bool)Prop(controller, "IsInteractingWithObject"), "Cannot switch with hands occupied");
        Call(interaction, "ReleaseHeldObject");
        ((Component)box).GetComponent<Rigidbody>().position = new Vector3(8f, 1f, -5f);
        Call(controller, "TryInteract");
        Assert.AreSame(lamp, Prop(controller, "ActiveTarget"));
        Assert.IsTrue((bool)Prop(interaction, "ExternalInteractionLocked"));
        Assert.IsTrue((bool)Prop(lamp, "IsOn"), "Input only reserves; contact changes the state");
        Assert.IsFalse((bool)Call(lamp, "TryReserve", other, false), "Partner cannot reserve the same lamp");
        Assert.IsFalse((bool)Call(lamp, "PerformReservedAction", other), "Other player's contact is rejected");
        for (float until = Time.time + 7f; (bool)Prop(lamp, "IsOn") && Time.time < until;) yield return null;
        Assert.IsFalse((bool)Prop(lamp, "IsOn"), "Actual DoorPush clip contact must switch lamp off");
        Assert.IsFalse(light.enabled);
        Assert.That(Vector3.Distance(player.GetComponent<Rigidbody>().position, point.position), Is.LessThan(.025f),
            "Alignment must bring the player to the selected point");
        Capture("Temp/ObjectInteractionLampOff.png", lamp.transform);
        Call(controller, "ActAtContact");
        Assert.IsFalse((bool)Prop(lamp, "IsOn"), "Duplicate contact must not toggle twice");
        yield return WaitIdle(controller);
        Assert.IsFalse((bool)Prop(interaction, "ExternalInteractionLocked"));
        Capture("Temp/ObjectInteractionLampOffIdle.png", lamp.transform);
        Call(controller, "TryInteract");
        yield return WaitIdle(controller);
        Assert.IsTrue((bool)Prop(lamp, "IsOn"));
        Assert.IsTrue(light.enabled);
        Capture("Temp/ObjectInteractionLampOn.png", lamp.transform);
        Call(controller, "TryInteract");
        ((Behaviour)controller).enabled = false;
        Assert.IsTrue((bool)Prop(lamp, "Available"), "Cancellation releases reservation");
        Assert.IsTrue((bool)Prop(lamp, "IsOn"), "Cancel before contact does not toggle");
        Assert.IsFalse((bool)Prop(interaction, "ExternalInteractionLocked"));
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator DoorPushPullStillWorksWithSharedDispatcher()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        var player = GameObject.Find("Player1");
        var controller = Controller(player);
        var door = GameObject.Find("Demo_InteractiveDoor").GetComponent(T("HGame.Objects.DoorInteractable"));
        foreach (int side in new[] { 0, 0, 1, 1 })
        {
            bool before = (bool)Prop(door, "IsOpen");
            AtPoint(player, (Transform)Call(door, "GetInteractionPoint", side));
            // Walk up beside the leaf, without standing exactly on the animation point.
            var offsetPoint = (Transform)Call(door, "GetInteractionPoint", side);
            Vector3 approach = offsetPoint.position + offsetPoint.right * .5f - offsetPoint.forward * .15f;
            approach.y = .68f;
            player.transform.position = approach;
            player.GetComponent<Rigidbody>().position = approach;
            Physics.SyncTransforms();
            for (float until = Time.time + .35f; Time.time < until;) yield return null;
            var rb = player.GetComponent<Rigidbody>();
            var point = (Transform)Call(door, "GetInteractionPoint", side);
            var animator = player.GetComponent<Animator>();
            Debug.Log("DOOR scenario side=" + side + " open=" + before + " available=" + Prop(door, "Available") +
                " player=" + rb.position + " point=" + point.position + " transition=" + animator.IsInTransition(0) +
                " state=" + animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
            Vector3 delta = point.position - rb.position; delta.y = 0f;
            if (delta.magnitude > .01f && rb.SweepTest(delta.normalized, out var obstacle, delta.magnitude, QueryTriggerInteraction.Ignore))
                Debug.Log("DOOR alignment blocked by " + obstacle.collider.name + " delta=" + delta);
            Call(controller, "TryInteract");
            Assert.AreSame(door, Prop(controller, "ActiveTarget"), "Must select door from side " + side + " open=" + before);
            Assert.AreEqual(before, Prop(door, "IsOpen"));
            yield return WaitIdle(controller);
            Assert.AreEqual(!before, Prop(door, "IsOpen"), "Door contact must open/close from either side");
            Assert.IsTrue((bool)Prop(door, "Available"));
            Assert.IsFalse((bool)Prop(player.GetComponent(T("HPlayer.InteractionController")), "ExternalInteractionLocked"));
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator DoorOpensAwayAndClosesWithoutChangingSides()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        var player = GameObject.Find("Player1");
        var controller = Controller(player);
        var door = GameObject.Find("Demo_InteractiveDoor").GetComponent(T("HGame.Objects.DoorInteractable"));
        var leaf = (Rigidbody)door.GetType().GetField("hingeBody", Flags).GetValue(door);
        Quaternion closed = leaf.rotation;
        float firstYaw = 0f;
        foreach (int side in new[] { 0, 1 })
        {
            var point = (Transform)Call(door, "GetInteractionPoint", side);
            AtPoint(player, point);
            for (float until = Time.time + .35f; Time.time < until;) yield return null;
            Call(controller, "TryInteract");
            Assert.AreSame(door, Prop(controller, "ActiveTarget"));
            yield return WaitIdle(controller);
            Assert.IsTrue((bool)Prop(door, "IsOpen"));
            Vector3 openingPosition = player.GetComponent<Rigidbody>().position;
            Assert.That(Vector3.Distance(openingPosition, point.position), Is.LessThan(.05f));
            Assert.AreSame(point, Call(door, "GetInteractionPoint", side), "The opener keeps the same stationary anchor for closing");
            float yaw = Vector3.SignedAngle(closed * Vector3.right, leaf.rotation * Vector3.right, Vector3.up);
            Assert.That(Mathf.Abs(yaw), Is.GreaterThan(70f));
            if (side == 0) firstYaw = yaw;
            else Assert.Less(firstYaw * yaw, 0f, "Opening from the other side reverses the swing");
            for (float until = Time.time + .3f; Time.time < until;) yield return null;
            // No teleport or walking between opening and closing.
            Call(controller, "TryInteract");
            Assert.AreSame(door, Prop(controller, "ActiveTarget"));
            yield return WaitIdle(controller);
            Assert.IsFalse((bool)Prop(door, "IsOpen"));
            Assert.That(Vector3.Distance(openingPosition, player.GetComponent<Rigidbody>().position), Is.LessThan(.05f));
            Assert.That(Quaternion.Angle(closed, leaf.rotation), Is.LessThan(1f));
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator DoorClosesFromOppositeSideOutsideTheLeafSweep()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        var player = GameObject.Find("Player1");
        var controller = Controller(player);
        var body = player.GetComponent<Rigidbody>();
        var capsule = player.GetComponent<CapsuleCollider>();
        var door = GameObject.Find("Demo_InteractiveDoor").GetComponent(T("HGame.Objects.DoorInteractable"));
        var leaf = (Rigidbody)door.GetType().GetField("hingeBody", Flags).GetValue(door);
        var shape = leaf.GetComponentInChildren<BoxCollider>();
        foreach (int openingSide in new[] { 0, 1 })
        foreach (float offset in new[] { -.35f, .35f })
        {
            AtPoint(player, (Transform)Call(door, "GetInteractionPoint", openingSide));
            for (float until = Time.time + .35f; Time.time < until;) yield return null;
            Call(controller, "TryInteract");
            yield return WaitIdle(controller);
            Assert.IsTrue((bool)Prop(door, "IsOpen"));
            var oppositeAnchor = (Transform)door.GetType().GetField(openingSide == 0 ? "closedBackPoint" : "closedFrontPoint", Flags).GetValue(door);
            Vector3 approach = oppositeAnchor.position + oppositeAnchor.right * offset;
            approach.y = .68f;
            player.transform.position = approach; body.position = approach;
            body.velocity = Vector3.zero;
            Physics.SyncTransforms();
            for (float until = Time.time + .3f; Time.time < until;) yield return null;
            Vector3 destination = ((Transform)Call(door, "GetInteractionPoint", 1 - openingSide)).position;
            Call(controller, "TryInteract");
            Assert.AreSame(door, Prop(controller, "ActiveTarget"), "opener=" + openingSide + " offset=" + offset);
            for (float deadline = Time.time + 9f; (bool)Prop(controller, "IsInteractingWithObject") && Time.time < deadline;)
            {
                yield return null;
                bool overlaps = Physics.ComputePenetration(capsule, body.position, body.rotation,
                    shape, shape.transform.position, shape.transform.rotation, out var direction, out float depth);
                Assert.IsFalse(overlaps && depth > .025f, "Closing approach must go around the leaf, depth=" + depth);
            }
            Assert.IsFalse((bool)Prop(controller, "IsInteractingWithObject"));
            Assert.IsFalse((bool)Prop(door, "IsOpen"));
            destination.y = body.position.y;
            Assert.That(Vector3.Distance(destination, body.position), Is.LessThan(.08f));
            Capture("Temp/DoorOppositeClose.png", door.transform);
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator DoorReachRejectsWallsAndDistance()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        var player = GameObject.Find("Player1");
        var controller = Controller(player);
        var door = GameObject.Find("Demo_InteractiveDoor").GetComponent(T("HGame.Objects.DoorInteractable"));
        var point = (Transform)Call(door, "GetInteractionPoint", 0);
        Vector3 start = point.position - point.forward * .7f;
        start.y = .68f;
        player.transform.position = start;
        player.GetComponent<Rigidbody>().position = start;
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = (start + point.position) * .5f;
        wall.transform.rotation = point.rotation;
        wall.transform.localScale = new Vector3(1.5f, 3f, .08f);
        Physics.SyncTransforms();
        Call(controller, "TryInteract");
        Assert.IsNull(Prop(controller, "ActiveTarget"), "A separate wall must block approach to the door");
        UnityEngine.Object.Destroy(wall);
        player.transform.position = start - point.forward * 3f;
        player.GetComponent<Rigidbody>().position = player.transform.position;
        Physics.SyncTransforms();
        Call(controller, "TryInteract");
        Assert.IsNull(Prop(controller, "ActiveTarget"), "Doors beyond reach must not activate");
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator PushableDoorOpensFromPlayerContactOnBothSides()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        yield return new EnterPlayMode();
        var player = GameObject.Find("Player1");
        var rb = player.GetComponent<Rigidbody>();
        ((Behaviour)player.GetComponent(T("ThirdPersonPlayerController"))).enabled = false;
        GameObject.Find("Player2").GetComponent<Rigidbody>().position = new Vector3(8f, .68f, 8f);
        var root = GameObject.Find("Demo_PushableDoor");
        Assert.IsNull(root.GetComponent(T("HGame.Objects.DoorInteractable")), "Push door does not require an interaction action");
        var hinge = root.GetComponentInChildren<HingeJoint>();
        var leaf = hinge.GetComponent<Rigidbody>();
        Quaternion closed = leaf.rotation;
        Vector3 center = leaf.GetComponentInChildren<Collider>().bounds.center;
        float firstAngle = 0f;
        foreach (float side in new[] { -1f, 1f })
        {
            player = GameObject.Find(side < 0f ? "Player1" : "Player2");
            rb = player.GetComponent<Rigidbody>();
            var movement = player.GetComponent(T("ThirdPersonPlayerController"));
            float walkSpeed = (float)Prop(movement, "BaseMoveSpeed");
            ((Behaviour)movement).enabled = false;
            var other = GameObject.Find(side < 0f ? "Player2" : "Player1");
            other.transform.position = new Vector3(8f, .68f, 8f);
            other.GetComponent<Rigidbody>().position = other.transform.position;
            leaf.rotation = closed;
            leaf.velocity = leaf.angularVelocity = Vector3.zero;
            Vector3 start = new Vector3(center.x, .68f, center.z + side * .95f);
            player.transform.SetPositionAndRotation(start, Quaternion.LookRotation(Vector3.forward * -side));
            rb.position = start;
            rb.velocity = Vector3.zero;
            Physics.SyncTransforms();
            for (float until = Time.time + 3f; Time.time < until;)
            {
                rb.velocity = Vector3.forward * (-side * walkSpeed);
                yield return null;
            }
            rb.velocity = Vector3.zero;
            Assert.That(Mathf.Abs(hinge.angle), Is.GreaterThan(20f), "Player contact must swing the leaf");
            Assert.That((rb.position.z - center.z) * -side, Is.GreaterThan(.1f), "Player must pass through the doorway");
            if (side < 0f) firstAngle = hinge.angle;
            else Assert.Less(firstAngle * hinge.angle, 0f, "The leaf swings in the direction of the push");
            Assert.IsFalse((bool)Prop(Controller(player), "IsInteractingWithObject"));
            Capture("Temp/PushableDoor.png", root.transform);
        }
        yield return new ExitPlayMode();
    }

    static void Capture(string path, Transform subject)
    {
        var camera = Camera.main;
        Vector3 position = camera.transform.position;
        Quaternion rotation = camera.transform.rotation;
        var previous = camera.targetTexture;
        var active = RenderTexture.active;
        var rt = new RenderTexture(1280, 720, 24);
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.transform.position = subject.position + new Vector3(1.5f, 1.3f, -2f);
            camera.transform.LookAt(subject.position + Vector3.up * .45f);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            texture.Apply();
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.targetTexture = previous;
            RenderTexture.active = active;
            UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(rt);
        }
    }
}
