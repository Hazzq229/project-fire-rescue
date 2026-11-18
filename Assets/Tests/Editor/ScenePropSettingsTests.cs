using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public class ScenePropSettingsTests
{
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType("TA.AdaptiveAnimation." + name)).First(t => t != null);
    static object P(object o, string name) => o.GetType().GetProperty(name).GetValue(o);

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
    }

    [UnityTest]
    public IEnumerator RenamedPropsUseAuthoredWeightTeamAndGripSettings()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity");
        var configs = UnityEngine.Object.FindObjectsOfType(T("ScenePropCarrySettings"), true).Cast<Component>().ToArray();
        Assert.AreEqual(207, configs.Length, "Saved destination inventory must be explicitly configured");
        int n = 0;
        foreach (var cfg in configs)
        {
            var so = new SerializedObject(cfg);
            if (cfg.name.StartsWith("Props_Computer_01"))
            {
                so.FindProperty("weightKg").floatValue = 23f;
                so.FindProperty("requiresTwoPlayers").boolValue = false;
                so.FindProperty("gripKind").enumValueIndex = 0;
            }
            else if (cfg.name.StartsWith("Props_Book_01"))
            {
                so.FindProperty("weightKg").floatValue = 77f;
                so.FindProperty("requiresTwoPlayers").boolValue = true;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            cfg.name = "ConfiguredItem_" + n++;
        }
        yield return new EnterPlayMode();
        var adapter = UnityEngine.Object.FindObjectOfType(T("AdaptiveSceneProps"));
        for (float deadline = Time.time + 5f; !(bool)P(adapter, "IsReady") && Time.time < deadline;) yield return null;
        Assert.IsTrue((bool)P(adapter, "IsReady"));
        var props = ((IEnumerable)adapter.GetType().GetField("Props").GetValue(adapter)).Cast<Component>().ToArray();
        Assert.AreEqual(207, props.Length, "Renaming cannot change candidate detection");
        int surface = 0, bounds = 0, bookOverrides = 0, computerOverrides = 0;
        foreach (var prop in props)
        {
            var visual = (Transform)P(prop, "Visual");
            var cfg = visual.GetComponent(T("ScenePropCarrySettings"));
            Assert.IsNotNull(cfg);
            float weight = (float)P(cfg, "WeightKg");
            bool team = (bool)P(cfg, "RequiresTwoPlayers");
            Assert.That(prop.GetComponent<Rigidbody>().mass, Is.EqualTo(weight).Within(.0001f));
            Assert.AreEqual(weight, (float)P(prop.GetComponent(T("CarryAnimationLoad")), "WeightKg"));
            Assert.AreEqual(team, (bool)P(prop, "RequiresTwoPlayers"));
            Assert.AreEqual(team, prop.GetComponent(T("CooperativeCarryObject")) != null);
            var grip = prop.GetComponent(T("ScenePropGripProvider"));
            bool fitted = (bool)P(grip, "HasSurfaceGrips");
            if (P(cfg, "GripKind").ToString() == "BoxSurface") { Assert.IsTrue(fitted); surface++; }
            else { Assert.IsFalse(fitted); bounds++; }
            if (weight == 77f) { Assert.IsTrue(team); bookOverrides++; }
            if (weight == 23f) { Assert.IsFalse(team); Assert.IsFalse(fitted); computerOverrides++; }
        }
        Assert.Greater(surface, 0); Assert.Greater(bounds, 0);
        Assert.Greater(bookOverrides, 0); Assert.Greater(computerOverrides, 0);
    }
}
