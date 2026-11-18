using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class GameOutcomeAuditTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static Type RuntimeType(string name)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name))
            .FirstOrDefault(candidate => candidate != null);
        Assert.IsNotNull(type, "Missing runtime type " + name);
        return type;
    }

    private static void SetField(Component component, string name, object value)
    {
        var field = component.GetType().GetField(name, Flags);
        Assert.IsNotNull(field, "Missing field " + component.GetType().Name + "." + name);
        field.SetValue(component, value);
    }

    private static object GetField(Component component, string name)
    {
        var field = component.GetType().GetField(name, Flags);
        Assert.IsNotNull(field, "Missing field " + component.GetType().Name + "." + name);
        return field.GetValue(component);
    }

    private static string CurrentState(Component manager) =>
        manager.GetType().GetProperty("CurrentState").GetValue(manager).ToString();

    private static GameObject CreateRound(out Component manager, out Component victim, out Component fire)
    {
        var root = new GameObject("Game outcome audit fixture");
        var victimObject = new GameObject("Unsafe victim");
        victimObject.transform.SetParent(root.transform);
        victim = victimObject.AddComponent(RuntimeType("VictimAI"));
        victimObject.GetComponent<Rigidbody>().isKinematic = true;
        victimObject.GetComponent<Rigidbody>().useGravity = false;

        var fireObject = new GameObject("Active fire");
        fireObject.transform.SetParent(root.transform);
        fireObject.transform.position = Vector3.right * 10f;
        fireObject.AddComponent<BoxCollider>().isTrigger = true;
        fire = fireObject.AddComponent(RuntimeType("Fire"));

        // The unsafe victim exists before GameManager.Start scans the scene, so
        // this rescue-only round cannot win while its fixture initializes.
        var managerObject = new GameObject("Game manager");
        managerObject.transform.SetParent(root.transform);
        manager = managerObject.AddComponent(RuntimeType("GameManager"));
        SetField(manager, "requireAllFiresExtinguishedToWin", false);
        SetField(manager, "maxFireDamageThreshold", 100f);
        SetField(manager, "damageIncreaseRate", 0f);
        return root;
    }

    private static void AssertRoundStarted(Component manager, Component victim, Component fire)
    {
        Assert.AreEqual("Playing", CurrentState(manager));
        Assert.IsFalse((bool)GetField(victim, "isSafe"));
        Assert.Contains(victim, (IList)GetField(manager, "activeVictims"), "Start must discover the actual victim");
        Assert.Contains(fire, (IList)GetField(manager, "activeFires"), "Fire.Start must register the active fire");
        Assert.IsTrue(((Behaviour)fire).isActiveAndEnabled);
        Assert.Greater(Time.deltaTime, 0f, "A positive frame duration is required for this controlled gameplay tick");
    }

    private static void RescueAndRunOneDamageTick(Component manager, Component victim, float damageBeforeTick)
    {
        SetField(manager, "currentFireDamage", damageBeforeTick);
        // Use the same Update path as the player loop. Scaling the rate to this
        // frame adds exactly one damage point without depending on editor FPS.
        SetField(manager, "damageIncreaseRate", 1f / Time.deltaTime);
        SetField(victim, "isSafe", true);
        manager.GetType().GetMethod("Update", Flags).Invoke(manager, null);
    }

    [UnityTest]
    public IEnumerator RescueOnDamageLimitFrameKeepsGameOver()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        Time.timeScale = 1f;
        var root = CreateRound(out var manager, out var victim, out var fire);
        try
        {
            yield return null;
            AssertRoundStarted(manager, victim, fire);
            RescueAndRunOneDamageTick(manager, victim, 99.5f);

            Assert.That((float)GetField(manager, "currentFireDamage"), Is.EqualTo(100f).Within(.0001f));
            Assert.AreEqual("GameOver", CurrentState(manager),
                "Reaching the damage limit must retain the loss when the final victim becomes safe in the same tick");
            yield return null;
            Assert.AreEqual("GameOver", CurrentState(manager), "A terminal loss must remain stable on the next frame");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator RescueBeforeDamageLimitStillWinsWithActiveFire()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        Time.timeScale = 1f;
        var root = CreateRound(out var manager, out var victim, out var fire);
        try
        {
            yield return null;
            AssertRoundStarted(manager, victim, fire);
            RescueAndRunOneDamageTick(manager, victim, 20f);

            Assert.That((float)GetField(manager, "currentFireDamage"), Is.EqualTo(21f).Within(.0001f));
            Assert.IsTrue(((Behaviour)fire).isActiveAndEnabled, "Rescue-only mode permits an active fire at victory");
            Assert.AreEqual("Victory", CurrentState(manager));
            yield return null;
            Assert.AreEqual("Victory", CurrentState(manager), "A terminal victory must remain stable on the next frame");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
        yield return new ExitPlayMode();
    }

    [UnityTearDown]
    public IEnumerator ExitPlayModeAfterFailure()
    {
        if (Application.isPlaying)
        {
            Time.timeScale = 1f;
            yield return new ExitPlayMode();
        }
    }
}
