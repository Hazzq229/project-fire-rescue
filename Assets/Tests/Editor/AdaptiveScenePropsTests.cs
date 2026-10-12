using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AdaptiveScenePropsTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static object P(object o, string name) => o.GetType().GetProperty(name).GetValue(o);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Flags).Invoke(o, args);
    static Component C(GameObject go, string type) => go.GetComponent(T(type));

    [UnityTest]
    public IEnumerator ScenePropsOwnershipFormationAndInput()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Gameloop_Demo.unity");
        yield return new EnterPlayMode();
        yield return null; yield return null;
        var adapter = C(GameObject.Find("FR_Level_1"), "TA.AdaptiveAnimation.AdaptiveSceneProps");
        Assert.IsTrue((bool)P(adapter, "IsReady"));
        var props = UnityEngine.Object.FindObjectsOfType(T("TA.AdaptiveAnimation.AdaptiveSceneProp"));
        Assert.Greater(props.Length, 100, "The complete level must be adapted, not only demo boxes");
        Assert.NotNull(GameObject.Find("Carry_Adaptive_Lamp"), "The standalone demo lamp must also support carry");
        Assert.NotNull(GameObject.Find("Carry_Props_FireTruck"), "All level Props are included in this demo");
        Component book = null, computer = null, table = null;
        var inventory = new List<string> { "Object,Category,TwoPlayers,Width,Height,Length" };
        foreach (Component prop in props)
        {
            Assert.Less(Vector3.Distance(Vector3.one, prop.transform.lossyScale), .0001f, prop.name);
            Assert.AreEqual(1, prop.GetComponentsInChildren<Rigidbody>(true).Length, prop.name);
            Assert.AreEqual(1, prop.GetComponentsInChildren(T("HInteractions.Liftable"), true).Length, prop.name);
            Assert.IsNotNull(C(prop.gameObject, "TA.AdaptiveAnimation.CarryAnimationLoad"));
            Assert.IsNotNull(C(prop.gameObject, "TA.AdaptiveAnimation.ScenePropGripProvider"));
            var dimensions = (Vector3)P(prop, "Dimensions");
            inventory.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2},{3:F3},{4:F3},{5:F3}",
                prop.name, P(prop, "Category"), P(prop, "RequiresTwoPlayers"), dimensions.x, dimensions.y, dimensions.z));
            if (prop.name == "Carry_Props_Book_01") book = prop;
            if (prop.name == "Carry_Props_Computer_01") computer = prop;
            if (prop.name == "Carry_Props_Table_01") table = prop;
        }
        Assert.NotNull(book); Assert.NotNull(computer); Assert.NotNull(table);
        System.IO.File.WriteAllLines("Temp/AdaptiveSceneProps-inventory.csv", inventory);
        Assert.IsFalse((bool)P(book, "RequiresTwoPlayers"));
        Assert.AreEqual("Medium", P(computer, "Category").ToString());
        Assert.IsTrue((bool)P(table, "RequiresTwoPlayers"));
        var a = C(GameObject.Find("Player1"), "HPlayer.InteractionController");
        var b = C(GameObject.Find("Player2"), "HPlayer.InteractionController");
        var item = C(book.gameObject, "HInteractions.Liftable");
        Call(a, "PickUpObject", item);
        Assert.AreSame(item, P(a, "HeldObject"));
        Call(b, "PickUpObject", item);
        Assert.IsNull(P(b, "HeldObject"), "A held small object cannot be stolen");
        Call(a, "ReleaseHeldObject");
        Assert.IsFalse(book.GetComponent<Rigidbody>().isKinematic, "Released props must support throwing and floor physics");

        Call(a, "PickUpObject", C(computer.gameObject, "HInteractions.Liftable"));
        for (float until = Time.time + 1f; Time.time < until;) yield return null;
        var grip = a.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectGripController"));
        CooperativeHoseTests.Capture("Temp/AdaptiveSceneProps-computer.png", a.transform.position + Vector3.up * .4f);
        Assert.Greater((float)grip.GetType().GetField("currentWeight", Flags).GetValue(grip), .95f,
            "Computer hand targets must stay within arm reach: " +
            grip.GetType().GetField("status", Flags).GetValue(grip) + " left " +
            grip.GetType().GetField("leftDistance", Flags).GetValue(grip) + " / " +
            grip.GetType().GetField("leftMaximumReach", Flags).GetValue(grip) + " right " +
            grip.GetType().GetField("rightDistance", Flags).GetValue(grip) + " / " +
            grip.GetType().GetField("rightMaximumReach", Flags).GetValue(grip));
        CooperativeHoseTests.CheckHandContact(grip);
        CooperativeHoseTests.Capture("Temp/AdaptiveSceneProps-computer.png", a.transform.position + Vector3.up * .4f);
        Call(a, "ReleaseHeldObject");

        var team = C(table.gameObject, "TA.AdaptiveAnimation.CooperativeCarryObject");
        table.GetComponent<Rigidbody>().position = new Vector3(30, 5, 30);
        table.GetComponent<Rigidbody>().rotation = Quaternion.identity;
        Physics.SyncTransforms();
        Type role = T("TA.AdaptiveAnimation.CarryRole");
        a.GetComponent<Rigidbody>().position = (Vector3)Call(team, "GetStance", Enum.Parse(role, "Front"), 5f);
        b.GetComponent<Rigidbody>().position = (Vector3)Call(team, "GetStance", Enum.Parse(role, "Back"), 5f);
        Physics.SyncTransforms();
        Call(a, "PickUpObject", team); Call(b, "PickUpObject", team);
        Assert.AreSame(team, P(a, "HeldObject")); Assert.AreSame(team, P(b, "HeldObject"));
        Assert.AreEqual("Front", Call(team, "GetRole", a).ToString());
        Assert.AreEqual("Back", Call(team, "GetRole", b).ToString());
        Assert.Less(Vector3.Angle(table.GetComponent<Rigidbody>().rotation * Vector3.up, Vector3.up), .1f,
            "Second pickup levels the furniture immediately");
        for (float until = Time.time + 2f; Time.time < until;) yield return null;
        foreach (var player in new[] { a, b })
        {
            var teamGrip = player.GetComponentInChildren(T("TA.AdaptiveAnimation.HeldObjectGripController"));
            Assert.Greater((float)teamGrip.GetType().GetField("currentWeight", Flags).GetValue(teamGrip), .95f,
                "Both table operators must maintain reachable grips: " + player.name);
            CooperativeHoseTests.CheckHandContact(teamGrip);
        }
        Call(a, "ReleaseHeldObject"); Assert.IsNull(P(b, "HeldObject"));
        Assert.IsFalse(table.GetComponent<Rigidbody>().isKinematic);

        var input = C(b.gameObject, "HPlayer.LocalPlayerInput");
        // Read real InputAction bindings, without depending on keyboard focus or synthetic user input.
        var aim = input.GetType().GetField("hoseAim", Flags).GetValue(input);
        var paths = new List<string>();
        foreach (var binding in (IEnumerable)P(aim, "bindings")) paths.Add((string)P(binding, "path"));
        foreach (string key in new[] { "minus", "p", "leftBracket", "rightBracket" })
            Assert.Contains("<Keyboard>/" + key, paths);
        foreach (string field in new[] { "hoseExtend", "hoseRetract" })
        {
            var action = input.GetType().GetField(field, Flags).GetValue(input);
            paths.Clear();
            foreach (var binding in (IEnumerable)P(action, "bindings")) paths.Add((string)P(binding, "path"));
            Assert.Contains("<Keyboard>/" + (field == "hoseExtend" ? "n" : "m"), paths);
        }
        yield return new ExitPlayMode();
    }
}
