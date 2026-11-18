using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// Use the saved menu and real Input System/UI events. These checks deliberately
// do not bypass missing selection by invoking the Play button's callback.
public class GameplayFlowAuditTests
{
    const string MenuScene = "Assets/Scenes/MainMenu.unity";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(name)).First(t => t != null);
    static object P(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
    static object F(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethods(Flags)
        .First(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v))
        .Invoke(value, args);
    static object SessionCall(string name, params object[] args) => T("LocalCoopSession")
        .GetMethod(name, BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
    static Component Menu() => (Component)Object.FindObjectOfType(T("MainMenuUIManager"));
    static GameObject Panel(Component menu, string name) => (GameObject)F(menu, name);
    static GameObject Selected()
    {
        var system = T("UnityEngine.EventSystems.EventSystem").GetProperty("current").GetValue(null);
        Assert.IsNotNull(system, "The saved menu needs an EventSystem");
        return (GameObject)P(system, "currentSelectedGameObject");
    }
    static void Keys(params string[] keys) => typeof(CooperativeHoseTests)
        .GetMethod("KeyboardState", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { keys });
    static void ReleaseKeyboard() => typeof(CooperativeHoseTests)
        .GetMethod("ResetKeyboard", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    static IEnumerator Wait(float seconds)
    {
        for (float until = Time.time + seconds; Time.time < until;)
            yield return null;
    }
    static IEnumerator Press(string key)
    {
        Keys(key); yield return Wait(.1f);
        Keys(); yield return Wait(.1f);
    }
    static Vector2 ReadMove(object playerInput)
    {
        var action = Call(P(playerInput, "actions"), "FindAction", "Move", false);
        return (Vector2)action.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == "ReadValue" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
            .MakeGenericMethod(typeof(Vector2)).Invoke(action, null);
    }
    static Component AssertUsableSelection(GameObject panel)
    {
        var selected = Selected();
        Assert.IsNotNull(selected, "Opening a panel must select a button for keyboard/gamepad navigation");
        Assert.IsTrue(selected.activeInHierarchy, "The selected control must be visible");
        Assert.IsTrue(selected.transform.IsChildOf(panel.transform), "Selection must belong to the visible panel");
        var button = selected.GetComponent(T("UnityEngine.UI.Button"));
        Assert.IsNotNull(button, "Selection must be an actionable menu button");
        Assert.IsTrue((bool)P(button, "interactable"), "Selection must become usable after players join");
        return button;
    }
    static IEnumerator AssertKeyboardNavigation(GameObject panel)
    {
        var before = AssertUsableSelection(panel);
        string key = null;
        Component neighbor = null;
        foreach (var direction in new[] { "Right", "Down", "Left", "Up" })
        {
            neighbor = (Component)Call(before, "FindSelectableOn" + direction);
            if (neighbor != null && neighbor.gameObject.activeInHierarchy && (bool)P(neighbor, "interactable"))
            {
                key = direction + "Arrow";
                break;
            }
        }
        Assert.IsNotNull(key, "The character selection needs a reachable neighboring control");
        yield return Press(key);
        Assert.AreSame(neighbor.gameObject, Selected(), "Actual UI Navigate input must move the selection");
        AssertUsableSelection(panel);
    }
    static IEnumerator CheckRepeatedJoin(string firstKey, string otherKey, string firstScheme, string otherScheme)
    {
        var menu = Menu();
        Assert.IsNotNull(menu);
        Call(menu, "ShowCharacterSelect");
        yield return Press(firstKey);
        Assert.IsTrue((bool)SessionCall("IsJoined", 0));
        Assert.IsFalse((bool)SessionCall("IsJoined", 1));
        yield return Press(firstKey);
        Assert.IsFalse((bool)SessionCall("IsJoined", 1),
            "Repeating the joined keyboard scheme must not consume the other player's slot");
        yield return Press(otherKey);
        Assert.IsTrue((bool)SessionCall("IsJoined", 1), "The other keyboard scheme must still be allowed to share the device");
        var slot1 = SessionCall("GetPlayer", 0);
        var slot2 = SessionCall("GetPlayer", 1);
        Assert.AreEqual(firstScheme, P(slot1, "ControlScheme"));
        Assert.AreEqual(otherScheme, P(slot2, "ControlScheme"));
        var devices1 = (Array)P(slot1, "Devices");
        var devices2 = (Array)P(slot2, "Devices");
        Assert.AreEqual(1, devices1.Length);
        Assert.AreEqual(1, devices2.Length);
        Assert.AreSame(devices1.GetValue(0), devices2.GetValue(0), "Distinct keyboard schemes intentionally share one physical keyboard");
        var input1 = P(slot1, "LobbyInput");
        var input2 = P(slot2, "LobbyInput");
        Assert.AreNotSame(input1, input2);
        Keys(firstKey);
        T("UnityEngine.InputSystem.InputSystem").GetMethod("Update", Type.EmptyTypes).Invoke(null, null);
        Assert.Greater(ReadMove(input1).sqrMagnitude, .5f);
        Assert.Less(ReadMove(input2).sqrMagnitude, .0001f, "Player 2 must not read Player 1's movement keys");
        Keys(); yield return Wait(.1f);
        Keys(otherKey);
        T("UnityEngine.InputSystem.InputSystem").GetMethod("Update", Type.EmptyTypes).Invoke(null, null);
        Assert.Less(ReadMove(input1).sqrMagnitude, .0001f, "Player 1 must not read Player 2's movement keys");
        Assert.Greater(ReadMove(input2).sqrMagnitude, .5f);
        Keys(); yield return Wait(.1f);
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying)
        {
            ReleaseKeyboard();
            var menu = Menu();
            if (menu != null) Call(menu, "ShowMainMenu");
            SessionCall("Reset");
            yield return new ExitPlayMode();
        }
        SessionCall("Reset");
        EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity");
    }

    [Test]
    public void FirstEnabledBuildSceneIsMainMenu()
    {
        Assert.AreEqual(MenuScene, EditorBuildSettings.scenes.First(s => s.enabled).path,
            "A normal player launch must enter the menu that creates the local co-op session");
    }

    [UnityTest]
    public IEnumerator RepeatedWasdJoinPreservesArrowSlotAndIndependentInput()
    {
        EditorSceneManager.OpenScene(MenuScene);
        yield return new EnterPlayMode();
        ReleaseKeyboard(); yield return Wait(.3f);
        yield return CheckRepeatedJoin("W", "UpArrow", "KeyboardWASD", "KeyboardArrows");
    }

    [UnityTest]
    public IEnumerator RepeatedArrowJoinPreservesWasdSlotAndIndependentInput()
    {
        EditorSceneManager.OpenScene(MenuScene);
        yield return new EnterPlayMode();
        ReleaseKeyboard(); yield return Wait(.3f);
        yield return CheckRepeatedJoin("UpArrow", "W", "KeyboardArrows", "KeyboardWASD");
    }

    [UnityTest]
    public IEnumerator FreshKeyboardSubmitOpensCharacterSelectAndCanNavigateAfterJoining()
    {
        EditorSceneManager.OpenScene(MenuScene);
        yield return new EnterPlayMode();
        ReleaseKeyboard(); yield return Wait(.3f);
        yield return Press("Enter");
        var menu = Menu();
        Assert.IsTrue(Panel(menu, "selectCharacterPanel").activeInHierarchy,
            "A fresh menu must accept keyboard Submit on Play without a pointer click");
        yield return Press("W");
        yield return Press("UpArrow");
        Assert.IsTrue((bool)SessionCall("IsJoined", 0));
        Assert.IsTrue((bool)SessionCall("IsJoined", 1));
        yield return AssertKeyboardNavigation(Panel(menu, "selectCharacterPanel"));
    }

    [UnityTest]
    public IEnumerator CreditsAndMainMenuTransitionsRestoreKeyboardSubmitTarget()
    {
        EditorSceneManager.OpenScene(MenuScene);
        yield return new EnterPlayMode();
        ReleaseKeyboard(); yield return Wait(.3f);
        var menu = Menu();
        Call(menu, "ShowCredits");
        yield return null;
        AssertUsableSelection(Panel(menu, "creditsPanel"));
        yield return Press("Enter");
        Assert.IsTrue(Panel(menu, "mainMenuPanel").activeInHierarchy, "Credits Back must accept keyboard Submit");
        Assert.AreSame(((Component)F(menu, "playButton")).gameObject, Selected(), "Returning to the menu must restore Play selection");
        yield return Press("Enter");
        Assert.IsTrue(Panel(menu, "selectCharacterPanel").activeInHierarchy, "Play must remain keyboard-operable after changing panels");
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void WallOcclusionBindingToleratesUnspawnedSlots(int occupiedSlot)
    {
        // Keep the fixture inactive so Awake cannot fill absent slots through
        // the Editor-only direct-scene keyboard fallback. Exercise the same
        // binding routine that Awake calls after valid/failed player spawns.
        var fixture = new GameObject("Gameplay flow null-slot fixture");
        fixture.SetActive(false);
        var profile = ScriptableObject.CreateInstance(T("DisasterSquad.WallOcclusion.WallOcclusionProfile"));
        profile.GetType().GetField("enableHighlight").SetValue(profile, false);
        try
        {
            var wall = fixture.AddComponent(T("DisasterSquad.WallOcclusion.WallOcclusionController"));
            wall.GetType().GetProperty("Profile").SetValue(wall, profile);
            var spawner = fixture.AddComponent(T("GameplayPlayerSpawner"));
            spawner.GetType().GetField("wallOcclusionController", Flags).SetValue(spawner, wall);
            var players = (Array)F(spawner, "_spawnedPlayers");
            Transform playerTransform = null;
            if (occupiedSlot >= 0)
            {
                var player = new GameObject("Only spawned player");
                player.transform.SetParent(fixture.transform);
                players.SetValue(player.AddComponent(T("UnityEngine.InputSystem.PlayerInput")), occupiedSlot);
                playerTransform = player.transform;
            }
            Assert.DoesNotThrow(() => Call(spawner, "AssignWallOcclusionTargets"),
                "An absent/rejected player must not make wall-target binding throw");
            var targets = (IList)P(wall, "FocusTargets");
            Assert.AreEqual(occupiedSlot < 0 ? 0 : 1, targets.Count);
            if (playerTransform != null) Assert.AreSame(playerTransform, targets[0]);
        }
        finally
        {
            Object.DestroyImmediate(fixture);
            Object.DestroyImmediate(profile);
        }
    }
}
