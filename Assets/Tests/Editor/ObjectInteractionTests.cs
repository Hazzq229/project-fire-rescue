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
