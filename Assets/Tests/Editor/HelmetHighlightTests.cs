using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public class HelmetHighlightTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static object P(object o, string name) => o.GetType().GetProperty(name).GetValue(o);
    static void Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Flags).Invoke(o, args);
    static IEnumerator Wait(float seconds) { for (float end = Time.time + seconds; Time.time < end;) yield return null; }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity");
    }

    [UnityTest]
    public IEnumerator WallHighlightKeepsOnlyVisibleModelAndPreservesHelmetSkin()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity");
        yield return new EnterPlayMode(); yield return Wait(.5f);
        foreach (Behaviour controller in UnityEngine.Object.FindObjectsOfType(T("DisasterSquad.WallOcclusion.WallOcclusionController")))
            controller.enabled = false;
        foreach (string name in new[] { "Player1", "Player2" })
        {
            var player = GameObject.Find(name);
            var highlight = player.GetComponent(T("DisasterSquad.WallOcclusion.CharacterOutlineHighlight"));
            Assert.IsNotNull(highlight);
            var outlines = player.GetComponentsInChildren<Renderer>(true).Where(r => r.name.EndsWith("(Outline)")).ToArray();
            Assert.IsNotEmpty(outlines);
            foreach (var outline in outlines)
            {
                var source = outline.transform.parent.GetComponent<Renderer>();
                Assert.IsNotNull(source);
                Assert.IsTrue(source.enabled && source.gameObject.activeInHierarchy, "Hidden old model must not get an outline: " + source.name);
            }
            Assert.IsFalse(outlines.Any(r => r.name == "hat.011 (Outline)"));
            var helmet = player.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Topi_Pemadam");
            Assert.IsFalse(outlines.Any(r => r.name == "Topi_Pemadam (Outline)"), "The configured helmet must not get a yellow overlay");
            var mesh = helmet.sharedMesh; var rootBone = helmet.rootBone; var bones = helmet.bones;
            var materials = helmet.sharedMaterials;

            var go = new GameObject("Helmet occlusion test camera"); var camera = go.AddComponent<Camera>();
            Vector3 target = player.transform.position + Vector3.up * .5f;
            go.transform.position = target + new Vector3(0, 2, -4); go.transform.LookAt(target);
            var controller = go.AddComponent(T("DisasterSquad.WallOcclusion.WallOcclusionController"));
            var profile = ScriptableObject.CreateInstance(T("DisasterSquad.WallOcclusion.WallOcclusionProfile"));
            profile.GetType().GetField("highlightMode").SetValue(profile, Enum.Parse(T("DisasterSquad.WallOcclusion.HighlightMode"), "OnlyWhenOccluded"));
            controller.GetType().GetProperty("Profile").SetValue(controller, profile);
            Call(controller, "AddFocusTarget", player.transform);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Helmet test wall"; wall.transform.position = (target + go.transform.position) * .5f;
            wall.transform.localScale = new Vector3(2, 2, .2f);
            Call(controller, "AddWall", wall.GetComponent<Renderer>());
            Physics.SyncTransforms(); yield return Wait(.2f);
            Assert.IsTrue((bool)P(highlight, "IsHighlightVisible"), name + " wall detection still activates outline");
            Assert.IsTrue(outlines.Any(r => r.gameObject.activeInHierarchy), "Body outline still shows when occluded");
            wall.transform.position += Vector3.right * 20; Physics.SyncTransforms(); yield return Wait(.2f);
            Assert.IsFalse((bool)P(highlight, "IsHighlightVisible"), name + " clear space hides outline");
            CollectionAssert.AreEqual(materials, helmet.sharedMaterials, "Highlight must not replace the helmet material");
            Assert.AreSame(mesh, helmet.sharedMesh); Assert.AreSame(rootBone, helmet.rootBone);
            CollectionAssert.AreEqual(bones, helmet.bones);
            UnityEngine.Object.Destroy(go); UnityEngine.Object.Destroy(wall); UnityEngine.Object.Destroy(profile);
            yield return null;
        }
    }
}
