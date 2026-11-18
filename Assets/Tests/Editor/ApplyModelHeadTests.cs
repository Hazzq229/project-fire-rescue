using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class ApplyModelHeadTests
{
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static object F(object value, string name) => value.GetType().GetField(name).GetValue(value);
    static object P(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
    static void Act(string name, Component slot) => T("CharacterModelReplacement").GetMethod(name).Invoke(null, new object[] { slot });
    static Component Slot(GameObject go) => go.GetComponent(T("TA.AdaptiveAnimation.CharacterModelSlot"));
    static GameObject Visual(Component slot) => (GameObject)F(slot, "generatedVisuals");

    sealed class Shape
    {
        public Mesh mesh;
        public Bounds bounds;
        public Vector3[] worldVertices;
    }
    static Vector3[] Vertices(SkinnedMeshRenderer skin)
    {
        var mesh = new Mesh();
        try { skin.BakeMesh(mesh, true); return mesh.vertices.Select(skin.transform.TransformPoint).ToArray(); }
        finally { Object.DestroyImmediate(mesh); }
    }
    static Dictionary<string, Shape> Capture(GameObject visual)
    {
        var follower = visual.GetComponent(T("TA.AdaptiveAnimation.CharacterRigidHeadFollower"));
        Assert.IsNotNull(follower);
        var root = (Transform)P(follower, "RigidRoot");
        return visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.rootBone == root)
            .ToDictionary(s => s.name, s => new Shape { mesh = s.sharedMesh, bounds = s.localBounds, worldVertices = Vertices(s) });
    }
    static void Check(Component slot, Dictionary<string, Shape> expected, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var visual = Visual(slot);
        Assert.That(Vector3.Distance(visual.transform.localPosition, position), Is.LessThan(.00001f));
        Assert.That(Quaternion.Angle(visual.transform.localRotation, rotation), Is.LessThan(.001f));
        Assert.That(Vector3.Distance(visual.transform.localScale, scale), Is.LessThan(.00001f));
        Assert.IsNull(visual.GetComponent(T("TA.AdaptiveAnimation.CharacterHeadgearFit")), "Saved helmet must not be fitted twice or rebuilt at runtime");
        var follower = visual.GetComponent(T("TA.AdaptiveAnimation.CharacterRigidHeadFollower"));
        Assert.IsNotNull(follower);
        var root = (Transform)P(follower, "RigidRoot");
        Assert.AreSame(visual.transform, root.parent);
        var current = Capture(visual);
        CollectionAssert.AreEquivalent(expected.Keys, current.Keys);
        Assert.AreEqual(9, current.Count);
        foreach (var pair in expected)
        {
            var now = current[pair.Key];
            Assert.AreSame(pair.Value.mesh, now.mesh, pair.Key + " persistent mesh reference");
            Assert.IsTrue(AssetDatabase.Contains(now.mesh));
            Assert.AreEqual(pair.Value.bounds, now.bounds, pair.Key + " root-space culling bounds");
            Assert.AreEqual(pair.Value.worldVertices.Length, now.worldVertices.Length);
            float drift = 0f;
            for (int i = 0; i < now.worldVertices.Length; i++) drift = Mathf.Max(drift, Vector3.Distance(pair.Value.worldVertices[i], now.worldVertices[i]));
            Assert.Less(drift, .002f, pair.Key + " must retain the calibrated world geometry");
        }
    }

    [Test]
    public void ReapplyRestoreUndoAndPrefabReloadPreserveSavedHeadAndHelmet()
    {
        const string source = "Assets/Prefabs/Player.prefab";
        string temp = AssetDatabase.GenerateUniqueAssetPath("Assets/Tests/Editor/ApplyModelRoundtrip.prefab");
        var player = PrefabUtility.LoadPrefabContents(source);
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        try
        {
            var slot = Slot(player); var visual = Visual(slot);
            var expected = Capture(visual);
            var pos = visual.transform.localPosition; var rot = visual.transform.localRotation; var scale = visual.transform.localScale;
            var gameplay = ((Transform)F(slot, "modelRoot")).GetComponentsInChildren<Component>(true);
            var controller = player.GetComponent<Animator>().runtimeAnimatorController;
            int skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
            Act("Apply", slot);
            Check(slot, expected, pos, rot, scale);
            Assert.IsNotNull(F(slot, "savedRigidHead"));
            Act("Apply", slot);
            Check(slot, expected, pos, rot, scale);
            Assert.AreEqual(skins, Visual(slot).GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);
            Undo.PerformUndo();
            Check(slot, expected, pos, rot, scale);
            // Persistent assets must never be destroyed by legacy generated-mesh cleanup.
            slot.GetType().GetField("generatedMeshes").SetValue(slot, expected.Values.Select(s => s.mesh).ToArray());
            Act("Restore", slot);
            Assert.IsNull(Visual(slot));
            Assert.IsNotNull(F(slot, "savedRigidHead"), "Restore retains authoring data");
            foreach (var shape in expected.Values) Assert.IsTrue(AssetDatabase.Contains(shape.mesh));
            Act("Apply", slot);
            Check(slot, expected, pos, rot, scale);
            foreach (var component in gameplay) Assert.IsTrue(component, "Gameplay skeleton/rig components survive");
            Assert.AreSame(controller, player.GetComponent<Animator>().runtimeAnimatorController);
            PrefabUtility.SaveAsPrefabAsset(player, temp);
            Undo.RevertAllDownToGroup(group);
            PrefabUtility.UnloadPrefabContents(player); player = null;
            AssetDatabase.ImportAsset(temp, ImportAssetOptions.ForceUpdate);
            player = PrefabUtility.LoadPrefabContents(temp);
            Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup();
            slot = Slot(player);
            Check(slot, expected, pos, rot, scale);
            Act("Restore", slot); Act("Apply", slot);
            Check(slot, expected, pos, rot, scale);
            var before = Visual(slot);
            slot.GetType().GetField("headgearScale").SetValue(slot, (float)F(slot, "headgearScale") + .1f);
            Assert.Throws<TargetInvocationException>(() => Act("Apply", slot));
            Assert.AreSame(before, Visual(slot), "Invalid calibration fails before destroying the current appearance");
            Debug.Log("APPLY MODEL: nine persistent head/face/helmet meshes retained across repeated Apply, Undo, Restore and prefab save/reload; gameplay rig/controller retained.");
        }
        finally
        {
            if (player) { Undo.RevertAllDownToGroup(group); PrefabUtility.UnloadPrefabContents(player); }
            AssetDatabase.DeleteAsset(temp);
        }
    }
}
