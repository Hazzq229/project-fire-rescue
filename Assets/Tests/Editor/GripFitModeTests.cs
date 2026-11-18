using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public class GripFitModeTests
{
    static Type ProviderType => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType("TA.AdaptiveAnimation.BoxGripProvider")).First(t => t != null);
    static Component[] Providers() => UnityEngine.Object.FindObjectsOfType(ProviderType).Cast<Component>().ToArray();
    static float Value(Component c, string name) => new SerializedObject(c).FindProperty(name).floatValue;
    static void Settings(Component c, int mode, float height, float depth)
    {
        var so = new SerializedObject(c);
        so.FindProperty("fitMode").enumValueIndex = mode;
        so.FindProperty("gripHeight").floatValue = height;
        so.FindProperty("gripDepth").floatValue = depth;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    static Vector3[] Points(Component c)
    {
        object[] args = { Vector3.zero, Vector3.zero };
        var method = ProviderType.GetMethods().Single(m => m.Name == "TryGetGripPoints" && m.GetParameters().Length == 2);
        Assert.IsTrue((bool)method.Invoke(c, args));
        return new[] { (Vector3)args[0], (Vector3)args[1] };
    }
    static void AuthoredValues(Component c, float height, float depth)
    {
        Assert.AreEqual(height, Value(c, "gripHeight"), c.name + " authored height");
        Assert.AreEqual(depth, Value(c, "gripDepth"), c.name + " authored depth");
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator GameloopSavedGripsPreserveInspectorValuesOnPlay()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity");
        Assert.AreEqual(3, Providers().Length);
        foreach (var c in Providers())
        {
            SessionState.SetFloat("GripModeTest.Height." + c.name, Value(c, "gripHeight"));
            SessionState.SetFloat("GripModeTest.Depth." + c.name, Value(c, "gripDepth"));
        }
        yield return new EnterPlayMode();
        yield return null;
        Assert.AreEqual(3, Providers().Length);
        foreach (var c in Providers())
        {
            AuthoredValues(c, SessionState.GetFloat("GripModeTest.Height." + c.name, -1f),
                SessionState.GetFloat("GripModeTest.Depth." + c.name, -1f));
            Assert.AreEqual(0, new SerializedObject(c).FindProperty("fitMode").enumValueIndex,
                "Existing saved objects retain Auto Fit by default");
            Points(c);
        }
    }

    [UnityTest]
    public IEnumerator ManualAndAutoFitKeepAuthoredValuesAndSupportLiveSwitching()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity");
        yield return new EnterPlayMode();
        foreach (float scale in new[] { .5f, 1.5f })
        foreach (int initialMode in new[] { 0, 1 })
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.SetActive(false);
            cube.transform.position = new Vector3(30f, 2f, 30f);
            cube.transform.localScale = Vector3.one * scale;
            var provider = cube.AddComponent(ProviderType);
            Settings(provider, initialMode, .3f, .75f);
            cube.SetActive(true);
            yield return null;
            AuthoredValues(provider, .3f, .75f);
            for (int step = 0; step < 4; step++)
            {
                int mode = step % 2;
                Settings(provider, mode, .3f, .75f);
                var points = Points(provider);
                AuthoredValues(provider, .3f, .75f);
                foreach (var point in points)
                {
                    Vector3 local = cube.transform.InverseTransformPoint(point);
                    Assert.That(local.y, Is.EqualTo(mode == 1 ? -.2f : (scale < 1f ? .35f : -.42f)).Within(.0001f));
                    Assert.That(local.z, Is.EqualTo(mode == 1 ? .25f : -.5f).Within(.0001f));
                }
                Assert.That(Mathf.Abs(cube.transform.InverseTransformPoint(points[0]).x),
                    Is.EqualTo(mode == 1 ? .5f : Mathf.Min(.49f, .14f / scale)).Within(.0001f));
            }
            cube.SetActive(false); cube.SetActive(true);
            AuthoredValues(provider, .3f, .75f);
            UnityEngine.Object.Destroy(cube);
        }
    }
}
