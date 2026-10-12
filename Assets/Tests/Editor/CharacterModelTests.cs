using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class CharacterModelTests
{
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static object F(object o, string name) => o.GetType().GetField(name).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name).SetValue(o, value);
    static void Act(string method, Component slot) => T("CharacterModelReplacement").GetMethod(method).Invoke(null, new object[] { slot });
    static Component Configure(GameObject player)
    {
        var slot = player.GetComponent(T("TA.AdaptiveAnimation.CharacterModelSlot")) ?? player.AddComponent(T("TA.AdaptiveAnimation.CharacterModelSlot"));
        var model = player.transform.Find("IDLE");
        Set(slot, "modelRoot", model);
        Set(slot, "skeletonRoot", model.Find("metarig.009/Bone"));
        Set(slot, "replacementModel", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Arts/Character/IDLE.fbx"));
        return slot;
    }

    [Test]
    public void ReplacementPreservesRigGeometryAndSupportsUndoRestore()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity");
        var player = Object.Instantiate(GameObject.Find("Player1"));
        try
        {
            var slot = Configure(player);
            var components = player.GetComponentsInChildren<Component>(true);
            var skins = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var rendererCount = player.GetComponentsInChildren<Renderer>(true).Length;
            var animator = player.GetComponent<Animator>();
            var controller = animator.runtimeAnimatorController;
            Act("Apply", slot);
            foreach (var component in components) Assert.IsTrue(component, "Existing gameplay/rig component must survive");
            Assert.AreSame(controller, animator.runtimeAnimatorController);
            foreach (var original in skins)
            {
                Assert.IsFalse(original.enabled);
                var copy = player.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Single(s => s != original && s.sharedMesh == original.sharedMesh);
                CollectionAssert.AreEqual(original.bones, copy.bones);
                var a = new Mesh(); var b = new Mesh();
                try
                {
                    original.BakeMesh(a, true); copy.BakeMesh(b, true);
                    Assert.AreEqual(a.vertexCount, b.vertexCount);
                    var av = a.vertices; var bv = b.vertices;
                    for (int i = 0; i < av.Length; i++)
                        Assert.Less(Vector3.Distance(original.transform.TransformPoint(av[i]), copy.transform.TransformPoint(bv[i])), .001f,
                            original.name + " original=" + original.transform.position + "/" + original.transform.lossyScale +
                            " copy=" + copy.transform.position + "/" + copy.transform.lossyScale + " vertex=" + av[i] + "/" + bv[i]);
                }
                finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
            }
            Act("Apply", slot);
            Assert.AreEqual(rendererCount * 2, player.GetComponentsInChildren<Renderer>(true).Length, "Repeated apply must not leak renderers");
            Act("Restore", slot);
            Assert.AreEqual(rendererCount, player.GetComponentsInChildren<Renderer>(true).Length);
            Assert.IsTrue(skins.All(s => s.enabled));
            Act("Apply", slot); Undo.PerformUndo();
            Assert.AreEqual(rendererCount, player.GetComponentsInChildren<Renderer>(true).Length);
            Assert.IsTrue(skins.All(s => s.enabled));
            Set(slot, "replacementModel", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Capsule_PlayerTemplate.prefab"));
            Assert.Throws<TargetInvocationException>(() => Act("Apply", slot));
            Assert.AreEqual(rendererCount, player.GetComponentsInChildren<Renderer>(true).Length, "Invalid model must leave appearance untouched");
        }
        finally { Object.DestroyImmediate(player); }
    }

    [UnityTest]
    public IEnumerator ReplacedModelsKeepPickupAnimationAndHoseIK()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity");
        Act("Apply", Configure(GameObject.Find("Player1")));
        Act("Apply", Configure(GameObject.Find("Player2")));
        yield return new EnterPlayMode();
        var p1 = GameObject.Find("Player1"); var p2 = GameObject.Find("Player2");
        var front = GameObject.Find("HoseNozzle");
        Position(p1, new Vector3(front.transform.position.x, .39f, front.transform.position.z - .45f));
        Position(p2, p1.transform.position - Vector3.forward * 1.05f);
        for (float until = Time.time + .4f; Time.time < until;) yield return null;
        var a = p1.GetComponent(T("HPlayer.InteractionController"));
        Call(a, "BeginInteraction");
        for (float until = Time.time + 4f; Time.time < until && (bool)P(a,"IsPickingUp");) yield return null;
        Assert.IsNotNull(P(a, "HeldObject"), "Pickup animation event must still attach nozzle");
        var rear = GameObject.Find("Hose_RearGrip");
        Position(p2, new Vector3(rear.transform.position.x,.39f,rear.transform.position.z-.42f));
        for (float until = Time.time + .4f; Time.time < until;) yield return null;
        var b = p2.GetComponent(T("HPlayer.InteractionController"));
        Call(b,"BeginInteraction");
        for (float until = Time.time + 4f; Time.time < until && (bool)P(b,"IsPickingUp");) yield return null;
        Assert.IsNotNull(P(b,"HeldObject"));
        for (float until = Time.time + 1f; Time.time < until;) yield return null;
        foreach (var player in new[] {p1,p2})
        {
            var slot = player.GetComponent(T("TA.AdaptiveAnimation.CharacterModelSlot"));
            Assert.IsNotNull(F(slot,"generatedVisuals"));
            CooperativeHoseTests.CheckHandContact(player.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectGripController")));
            Assert.AreEqual(Animator.StringToHash("HoseHold"), player.GetComponent<Animator>().GetCurrentAnimatorStateInfo(2).shortNameHash);
        }
        CooperativeHoseTests.Capture("Temp/CharacterModel-Hose.png", p1.transform.position + Vector3.up * .4f);
        yield return new ExitPlayMode();
        Act("Restore", GameObject.Find("Player1").GetComponent(T("TA.AdaptiveAnimation.CharacterModelSlot")));
        Act("Restore", GameObject.Find("Player2").GetComponent(T("TA.AdaptiveAnimation.CharacterModelSlot")));
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity");
    }
    static object P(object o,string n) => o.GetType().GetProperty(n).GetValue(o);
    static void Call(object o,string n) => o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(o,null);
    static void Position(GameObject p,Vector3 position)
    {
        var body=p.GetComponent<Rigidbody>(); body.position=position; body.rotation=Quaternion.identity; body.velocity=Vector3.zero;
        Physics.SyncTransforms();
    }
}
