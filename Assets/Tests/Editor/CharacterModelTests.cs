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
    static object F(object o, string name) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name).SetValue(o, value);
    static void Act(string method, Component slot) => T("CharacterModelReplacement").GetMethod(method).Invoke(null, new object[] { slot });
    static Component Configure(GameObject player)
    {
        var slot = player.GetComponent(T("TA.AdaptiveAnimation.CharacterModelSlot")) ?? player.AddComponent(T("TA.AdaptiveAnimation.CharacterModelSlot"));
        var model = player.transform.Find("IDLE");
        Set(slot, "modelRoot", model);
        Set(slot, "skeletonRoot", model.Find("metarig.009/Bone"));
        return slot;
    }

    [Test]
    public void ReplacementPreservesRigGeometryAndSupportsUndoRestore()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        var player = Object.Instantiate(GameObject.Find("Player1"));
        Undo.IncrementCurrentGroup();
        int testGroup = Undo.GetCurrentGroup();
        try
        {
            var slot = Configure(player);
            Act("Restore", slot);
            var components = player.GetComponentsInChildren<Component>(true);
            var skins = player.GetComponentsInChildren<Renderer>(true);
            var enabledStates = skins.Select(r => r.enabled).ToArray();
            var referenceBounds = GeometryBounds(skins);
            var rendererCount = player.GetComponentsInChildren<Renderer>(true).Length;
            var animator = player.GetComponent<Animator>();
            var controller = animator.runtimeAnimatorController;
            Act("Apply", slot);
            foreach (var component in components) Assert.IsTrue(component, "Existing gameplay/rig component must survive");
            Assert.AreSame(controller, animator.runtimeAnimatorController);
            Assert.IsTrue(skins.All(r => !r.enabled));
            var visual = (GameObject)F(slot, "generatedVisuals");
            var source = (GameObject)F(slot, "replacementModel");
            var actualBounds = GeometryBounds(visual.GetComponentsInChildren<Renderer>(true));
            Assert.That(actualBounds.size.y / referenceBounds.size.y, Is.InRange(.98f, 1.02f));
            Assert.That(Mathf.Abs(actualBounds.min.y - referenceBounds.min.y), Is.LessThan(.01f));
            Assert.That(Mathf.Abs(visual.transform.localScale.x - visual.transform.localScale.y), Is.LessThan(.00001f));
            Assert.That(Mathf.Abs(visual.transform.localScale.x - visual.transform.localScale.z), Is.LessThan(.00001f));
            foreach (var original in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var copy = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s => s.name == original.name);
                Assert.AreEqual(original.sharedMesh.vertexCount, copy.sharedMesh.vertexCount);
                CollectionAssert.AreEqual(original.bones.Select(b => b.name), copy.bones.Select(b => b.name));
                for (int i = 0; i < original.bones.Length; i++)
                {
                    Assert.AreEqual(original.bones[i].localPosition, copy.bones[i].localPosition);
                    Assert.AreEqual(original.bones[i].localScale, copy.bones[i].localScale);
                }
                if (original.name != (string)F(slot, "headgearMeshName")) Assert.AreSame(original.sharedMesh, copy.sharedMesh);
            }
            int appliedCount = player.GetComponentsInChildren<Renderer>(true).Length;
            Act("Apply", slot);
            Assert.AreEqual(appliedCount, player.GetComponentsInChildren<Renderer>(true).Length, "Repeated apply must not leak renderers");
            Act("Restore", slot);
            Assert.AreEqual(rendererCount, player.GetComponentsInChildren<Renderer>(true).Length);
            CollectionAssert.AreEqual(enabledStates, skins.Select(s => s.enabled));
            Act("Apply", slot); Undo.PerformUndo();
            Assert.AreEqual(rendererCount, player.GetComponentsInChildren<Renderer>(true).Length);
            CollectionAssert.AreEqual(enabledStates, skins.Select(s => s.enabled));
            Set(slot, "replacementModel", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Capsule_PlayerTemplate.prefab"));
            Assert.Throws<TargetInvocationException>(() => Act("Apply", slot));
            Assert.AreEqual(rendererCount, player.GetComponentsInChildren<Renderer>(true).Length, "Invalid model must leave appearance untouched");
        }
        finally
        {
            Undo.RevertAllDownToGroup(testGroup);
            Object.DestroyImmediate(player);
            Assert.IsEmpty(UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
                .Where(g => g.name == "CharacterAppearance"), "Undo must not leave an orphan appearance");
        }
    }

    [UnityTest]
    public IEnumerator ReplacedModelsKeepPickupAnimationAndHoseIK()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        Act("Apply", Configure(GameObject.Find("Player1")));
        Act("Apply", Configure(GameObject.Find("Player2")));
        yield return new EnterPlayMode();
        var p1 = GameObject.Find("Player1"); var p2 = GameObject.Find("Player2");
        var front = GameObject.Find("HoseNozzle");
        Position(p1, new Vector3(front.transform.position.x, .68f, front.transform.position.z - .45f));
        Position(p2, p1.transform.position - Vector3.forward * 1.05f);
        for (float until = Time.time + .4f; Time.time < until;) yield return null;
        var a = p1.GetComponent(T("HPlayer.InteractionController"));
        var visible = (GameObject)F(Slot(p1), "generatedVisuals");
        var hand = visible.GetComponentsInChildren<Transform>().First(t => t.name == "hand.L");
        Vector3 idleHand = p1.transform.InverseTransformPoint(hand.position);
        float motion = 0f;
        Call(a, "BeginInteraction");
        Assert.IsTrue((bool)P(a, "IsPickingUp"));
        for (float until = Time.time + 4f; Time.time < until && (bool)P(a,"IsPickingUp");)
        {
            yield return null;
            motion = Mathf.Max(motion, Vector3.Distance(idleHand, p1.transform.InverseTransformPoint(hand.position)));
        }
        Assert.Greater(motion, .03f, "Visible replacement must animate during pickup");
        Assert.IsNotNull(P(a, "HeldObject"), "Pickup animation event must still attach nozzle");
        var rear = GameObject.Find("Hose_RearGrip");
        Position(p2, new Vector3(rear.transform.position.x,.68f,rear.transform.position.z-.42f));
        for (float until = Time.time + .4f; Time.time < until;) yield return null;
        var b = p2.GetComponent(T("HPlayer.InteractionController"));
        Call(b,"BeginInteraction");
        for (float until = Time.time + 4f; Time.time < until && (bool)P(b,"IsPickingUp");) yield return null;
        Assert.IsNotNull(P(b,"HeldObject"));
        for (float until = Time.time + 1f; Time.time < until;) yield return null;
        var hose = front.GetComponent(T("TA.AdaptiveAnimation.CooperativeFireHose"));
        foreach (var aim in new[] {Vector2.zero, Vector2.up, Vector2.down})
        {
            Call(hose, "ApplyAim", a, aim, aim == Vector2.down ? .5f : .25f);
            for (float until = Time.time + .6f; Time.time < until;) yield return null;
            foreach (var player in new[] {p1,p2})
            {
                var slot = player.GetComponent(T("TA.AdaptiveAnimation.CharacterModelSlot"));
                Assert.IsNotNull(F(slot,"generatedVisuals"));
                var grip = player.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectGripController"));
                CooperativeHoseTests.CheckHandContact(grip);
                foreach (var side in new[] {"L", "R"})
                {
                    var constraint = F(grip, side == "L" ? "leftArmIK" : "rightArmIK");
                    Type constraintType = constraint.GetType();
                    FieldInfo dataField = null;
                    while (constraintType != null && dataField == null)
                    {
                        dataField = constraintType.GetField("m_Data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        constraintType = constraintType.BaseType;
                    }
                    var target = (Transform)P(dataField.GetValue(constraint), "target");
                    var tip = ((GameObject)F(slot, "generatedVisuals")).GetComponentsInChildren<Transform>().First(t => t.name == "hand." + side);
                    Assert.That(Vector3.Distance(tip.position, target.position), Is.LessThan(.015f), player.name + " visible hand " + side + " aim=" + aim);
                }
                Assert.AreEqual(Animator.StringToHash("HoseHold"), player.GetComponent<Animator>().GetCurrentAnimatorStateInfo(2).shortNameHash);
            }
        }
        foreach (var player in new[] { p1, p2 })
        {
            var slot = Slot(player);
            var visual = (GameObject)F(slot, "generatedVisuals");
            var helmetName = (string)F(slot, "headgearMeshName");
            Assert.AreEqual(1, visual.GetComponentsInChildren<SkinnedMeshRenderer>()
                .Count(r => r.enabled && r.name == helmetName), player.name + " must show its configured helmet");
        }
        CooperativeHoseTests.Capture("Temp/CharacterModel-Hose.png", p1.transform.position + Vector3.up * .4f);
        yield return new ExitPlayMode();
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
    }

    [Test]
    public void HelmetKeepsForeheadClearDuringIdleAndPickup()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/AdaptiveAnimation_Demo.unity");
        var player = Object.Instantiate(GameObject.Find("Player1"));
        try
        {
            var slot = Slot(player);
            var visual = (GameObject)F(slot, "generatedVisuals");
            var mirror = visual.GetComponent(T("TA.AdaptiveAnimation.CharacterModelAnimatorMirror"));
            var head = visual.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Kepala");
            var helmet = visual.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Topi_Pemadam");
            var source = ((GameObject)F(slot, "replacementModel")).GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Topi_Pemadam").sharedMesh;
            Assert.AreNotSame(source, helmet.sharedMesh, "Fit must use an instance mesh");
            foreach (var sample in new[] { new { path = "Idle.anim", time = 0f }, new { path = "PickupObject.anim", time = 1f }, new { path = "PickupObject.anim", time = 1.7f } })
            {
                AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Arts/Character/" + sample.path).SampleAnimation(player, sample.time);
                Call(mirror, "LateUpdate");
                var skull = WorldVertices(head);
                var shell = WorldVertices(helmet);
                var triangles = helmet.sharedMesh.triangles;
                var bounds = new Bounds(skull[0], Vector3.zero);
                foreach (var vertex in skull) bounds.Encapsulate(vertex);
                float maximumProtrusion = 0f;
                int checkedVertices = 0;
                foreach (var vertex in skull)
                {
                    if (vertex.y < bounds.center.y + bounds.size.y * .15f) continue;
                    Vector3 origin = new Vector3(bounds.center.x, vertex.y, bounds.center.z);
                    Vector3 ray = vertex - origin;
                    float distance = ray.magnitude;
                    if (distance < .0001f) continue;
                    float shellDistance = float.PositiveInfinity;
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        float hit = RayTriangle(origin, ray / distance, shell[triangles[i]], shell[triangles[i + 1]], shell[triangles[i + 2]]);
                        if (hit > 0f) shellDistance = Mathf.Min(shellDistance, hit);
                    }
                    if (float.IsPositiveInfinity(shellDistance)) continue;
                    checkedVertices++;
                    maximumProtrusion = Mathf.Max(maximumProtrusion, distance - shellDistance);
                }
                Assert.Greater(checkedVertices, 100, "Probe actual skull against the crown");
                Assert.That(maximumProtrusion, Is.LessThan(.003f), sample.path + " forehead must stay within the helmet crown");
            }
        }
        finally { Object.DestroyImmediate(player); }
    }

    static float RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 edge1 = b - a, edge2 = c - a;
        Vector3 p = Vector3.Cross(direction, edge2);
        float det = Vector3.Dot(edge1, p);
        if (Mathf.Abs(det) < .0000001f) return -1f;
        Vector3 t = origin - a;
        float u = Vector3.Dot(t, p) / det;
        if (u < 0f || u > 1f) return -1f;
        Vector3 q = Vector3.Cross(t, edge1);
        float v = Vector3.Dot(direction, q) / det;
        if (v < 0f || u + v > 1f) return -1f;
        return Vector3.Dot(edge2, q) / det;
    }
    static object P(object o,string n) => o.GetType().GetProperty(n).GetValue(o);
    static Component Slot(GameObject p) => p.GetComponent(T("TA.AdaptiveAnimation.CharacterModelSlot"));
    static object Call(object o,string n, params object[] args) => o.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
        .First(m => m.Name == n && m.GetParameters().Length == args.Length).Invoke(o,args);
    static void Position(GameObject p,Vector3 position)
    {
        p.transform.SetPositionAndRotation(position, Quaternion.identity);
        var body=p.GetComponent<Rigidbody>(); body.position=position; body.rotation=Quaternion.identity; body.velocity=Vector3.zero;
        Physics.SyncTransforms();
    }

    static Vector3[] WorldVertices(SkinnedMeshRenderer renderer)
    {
        var mesh = new Mesh();
        try
        {
            renderer.BakeMesh(mesh, true);
            return mesh.vertices.Select(renderer.transform.TransformPoint).ToArray();
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    static Bounds GeometryBounds(Renderer[] renderers)
    {
        bool first = true;
        var bounds = new Bounds();
        foreach (var renderer in renderers)
        {
            if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh && skin.sharedMesh.vertexCount > 0)
                foreach (var point in WorldVertices(skin))
                {
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            else if (renderer is MeshRenderer)
            {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
        }
        return bounds;
    }
}
