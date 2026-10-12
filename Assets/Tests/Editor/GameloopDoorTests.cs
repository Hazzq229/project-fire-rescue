using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class GameloopDoorTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static object P(object o, string name) => o.GetType().GetProperty(name).GetValue(o);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethods(Flags)
        .First(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p,i) => p.ParameterType.IsInstanceOfType(args[i])).All(v => v)).Invoke(o, args);
    static string Nearby(Vector3 point) => string.Join(", ", Physics.OverlapBox(point + Vector3.up * .6f,
        new Vector3(.32f,.6f,.32f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Select(c => c.name));

    [UnityTest]
    public IEnumerator AllDoorsOpenCloseFromBothSidesAtPlayerHeight()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity");
        yield return new EnterPlayMode();
        var player = GameObject.Find("Player1");
        var body = player.GetComponent<Rigidbody>();
        var controller = player.GetComponent(T("HPlayer.DoorInteractionController"));
        var other = GameObject.Find("Player2").GetComponent<Rigidbody>();
        other.position = new Vector3(30,.39f,30);
        for (float until = Time.time + .4f; Time.time < until;) yield return null;
        var doors = UnityEngine.Object.FindObjectsOfType(T("HGame.Objects.DoorInteractable"));
        Assert.AreEqual(7, doors.Length);
        foreach (Component door in doors)
        {
            var hinge = (Rigidbody)door.GetType().GetField("hingeBody", Flags).GetValue(door);
            Assert.IsTrue(hinge.isKinematic);
            Assert.IsTrue(hinge.GetComponentsInChildren<Collider>().Any(c => c.enabled && !c.isTrigger), door.name + " requires a solid moving collider");
            foreach (int side in new[] { 0, 0, 1, 1 })
            {
                bool before = (bool)P(door,"IsOpen");
                var point = (Transform)Call(door,"GetInteractionPoint",side);
                Assert.That(point.position.y, Is.EqualTo(.39f).Within(.015f), door.name);
                if (before) Assert.IsTrue(point.IsChildOf(hinge.transform), "Open points must follow the hinge");
                body.position = point.position; body.rotation = point.rotation; body.velocity = Vector3.zero;
                Physics.SyncTransforms();
                for (float until = Time.time + .3f; Time.time < until;) yield return null;
                // Input is intentionally gated during the outgoing push/pull -> idle transition.
                var animator = player.GetComponent<Animator>();
                for (float until = Time.time + 2f; animator.IsInTransition(0) && Time.time < until;) yield return null;
                Assert.IsFalse(animator.IsInTransition(0), "Door animation must return to an input-ready state");
                if (!before)
                {
                    bool blockedByDoor = false;
                    foreach (var hit in body.SweepTestAll(point.forward, 1f, QueryTriggerInteraction.Ignore))
                        if (hit.collider.transform.IsChildOf(hinge.transform)) blockedByDoor = true;
                    Assert.IsTrue(blockedByDoor, door.name + " closed collider must block the player's capsule");
                }
                Call(controller,"TryInteract");
                if (!ReferenceEquals(door, P(controller,"ActiveTarget")))
                    CooperativeHoseTests.Capture("Temp/GameloopDoor-failure.png", point.position);
                Assert.AreSame(door, P(controller,"ActiveTarget"), door.name + " side=" + side + " open=" + before +
                    " player=" + body.position + " point=" + point.position + " obstacles=" + Nearby(point.position) +
                    " transitioning=" + player.GetComponent<Animator>().IsInTransition(0));
                for (float until = Time.time + 8f; (bool)P(controller,"IsInteractingWithObject") && Time.time < until;) yield return null;
                Assert.IsFalse((bool)P(controller,"IsInteractingWithObject"), door.name + " must release player controls");
                Assert.AreEqual(!before,P(door,"IsOpen"),door.name + " animation contact must toggle the door");
            }
        }
        yield return new ExitPlayMode();
    }
}
