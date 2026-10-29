using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Behavior;
using UnityEngine;

public class NpcRelationshipTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Categories = "\"categories\":[{\"id\":\"trust\",\"initial\":3},{\"id\":\"annoyance\",\"initial\":0}]";
    static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    static object Json(string name, string json) => JsonUtility.FromJson(json, Runtime(name));
    static string State(int trust, int annoyance = 0) => "{\"values\":[{\"category\":\"trust\",\"value\":" + trust + "},{\"category\":\"annoyance\",\"value\":" + annoyance + "}]}";
    static object Response(int revision, int trust) => Json("NpcResponse", "{\"status\":\"completed\",\"revision\":" + revision + ",\"relationship\":" + State(trust) + "}");
    static ScriptableObject Definition()
    {
        var definition = ScriptableObject.CreateInstance(Runtime("NpcRelationshipDefinition"));
        JsonUtility.FromJsonOverwrite("{\"rules\":{" + Categories + "},\"greetings\":[" +
            "{\"priority\":40,\"name\":\"trusted\",\"greeting\":\"Good to see you, friend.\",\"conditions\":[{\"category\":\"trust\",\"comparison\":\"gte\",\"value\":40},{\"category\":\"annoyance\",\"comparison\":\"lt\",\"value\":10}]}," +
            "{\"priority\":100,\"name\":\"annoyed\",\"greeting\":\"Give me space.\",\"conditions\":[{\"category\":\"annoyance\",\"comparison\":\"gte\",\"value\":10}]}," +
            "{\"priority\":10,\"name\":\"wary\",\"greeting\":\"Keep your distance.\",\"conditions\":[{\"category\":\"trust\",\"comparison\":\"lt\",\"value\":0}]}," +
            "{\"priority\":20,\"name\":\"friendly\",\"greeting\":\"Welcome back.\",\"conditions\":[{\"category\":\"trust\",\"comparison\":\"gte\",\"value\":20}]}," +
            "{\"priority\":0,\"name\":\"neutral\",\"greeting\":\"\",\"conditions\":[]}]}", definition);
        return definition;
    }

    [TestCase(-1, 0, "wary", "Keep your distance.")]
    [TestCase(0, 0, "neutral", "Hello.")]
    [TestCase(20, 0, "friendly", "Welcome back.")]
    [TestCase(40, 0, "trusted", "Good to see you, friend.")]
    [TestCase(40, 10, "annoyed", "Give me space.")]
    public void GreetingsUseCategoryConditionsAndPriority(int trust, int annoyance, string band, string greeting)
    {
        var definition = Definition();
        try
        {
            object[] args = { Json("NpcRelationshipState", State(trust, annoyance)), "Hello.", null };
            Assert.That(definition.GetType().GetMethod("SelectGreeting").Invoke(definition, args), Is.EqualTo(greeting));
            Assert.That(args[2], Is.EqualTo(band));
        }
        finally { UnityEngine.Object.DestroyImmediate(definition); }
    }

    [Test]
    public void MissingCategoryUsesAuthoredInitialAndUnknownCategoryFails()
    {
        var definition = Definition();
        try
        {
            object[] args = { Json("NpcRelationshipState", "{}"), "trust", 0 };
            Assert.That(definition.GetType().GetMethod("TryGetValue").Invoke(definition, args), Is.EqualTo(true));
            Assert.That(args[2], Is.EqualTo(3));
            args[1] = "invented";
            Assert.That(definition.GetType().GetMethod("TryGetValue").Invoke(definition, args), Is.EqualTo(false));
            JsonUtility.FromJsonOverwrite("{\"greetings\":[{\"priority\":100,\"greeting\":\"Incorrect\",\"conditions\":[{\"category\":\"invented\",\"comparison\":\"gte\",\"value\":0}]}]}", definition);
            Assert.That(definition.GetType().GetMethod("SelectGreeting").Invoke(definition, new object[] { null, "Fallback", null }), Is.EqualTo("Fallback"));
        }
        finally { UnityEngine.Object.DestroyImmediate(definition); }
    }

    [Test]
    public void AllConditionsMustMatchAndTiedPriorityPreservesAuthoredOrder()
    {
        var definition = Definition();
        try
        {
            JsonUtility.FromJsonOverwrite("{\"greetings\":[{\"priority\":20,\"greeting\":\"Both\",\"conditions\":[{\"category\":\"trust\",\"comparison\":\"gte\",\"value\":20},{\"category\":\"annoyance\",\"comparison\":\"lt\",\"value\":10}]},{\"priority\":20,\"greeting\":\"Second\",\"conditions\":[]}]}", definition);
            var select = definition.GetType().GetMethod("SelectGreeting");
            Assert.That(select.Invoke(definition, new object[] { Json("NpcRelationshipState", State(20)), "Fallback", null }), Is.EqualTo("Both"));
            Assert.That(select.Invoke(definition, new object[] { Json("NpcRelationshipState", State(20, 10)), "Fallback", null }), Is.EqualTo("Second"));
        }
        finally { UnityEngine.Object.DestroyImmediate(definition); }
    }

    [Test]
    public void ProfileTransportCarriesCategoryEffectsAndRepeatWithoutGreetingPresentation()
    {
        var profile = ScriptableObject.CreateInstance(Runtime("NpcProfile"));
        var definition = Definition();
        try
        {
            JsonUtility.FromJsonOverwrite("{\"rules\":{" + Categories + ",\"rules\":[{\"id\":\"help\",\"source\":\"action\",\"evidenceKey\":\"need-met\",\"effects\":[{\"category\":\"trust\",\"delta\":7},{\"category\":\"annoyance\",\"delta\":-3}],\"repeat\":{\"maxApplications\":4,\"cooldownSeconds\":300}}]}}", definition);
            profile.GetType().GetField("relationship").SetValue(profile, definition);
            var transport = JsonUtility.ToJson(profile.GetType().GetMethod("ToData").Invoke(profile, null));
            Assert.That(transport, Does.Contain("\"evidenceKey\":\"need-met\"").And.Contain("\"delta\":7").And.Contain("\"delta\":-3").And.Contain("\"maxApplications\":4").And.Contain("\"cooldownSeconds\":300"));
            Assert.That(transport, Does.Not.Contain("Welcome back."));
        }
        finally { UnityEngine.Object.DestroyImmediate(profile); UnityEngine.Object.DestroyImmediate(definition); }
    }

    [Test]
    public void AcceptedStateAdvancesRevisionWithoutReplacingHistoryAndResetClearsIt()
    {
        var go = new GameObject("Relationship projection"); go.SetActive(false);
        try
        {
            var memory = go.AddComponent(Runtime("NpcMemory")); var type = memory.GetType();
            type.GetMethod("Awake", Hidden).Invoke(memory, null);
            var load = Json("NpcResponse", "{\"status\":\"completed\",\"revision\":1,\"hasSpoken\":true,\"history\":[{\"kind\":\"player_input\",\"text\":\"Hello\",\"delivery\":\"completed\"}],\"relationship\":" + State(10) + "}");
            type.GetMethod("Apply", Hidden).Invoke(memory, new[] { load });
            type.GetMethod("ApplyAcceptedState").Invoke(memory, new[] { Response(2, 30) });
            Assert.That(type.GetProperty("Revision").GetValue(memory), Is.EqualTo(2));
            Assert.That(type.GetProperty("HasSpoken").GetValue(memory), Is.True);
            Assert.That(Trust(memory), Is.EqualTo(30));
            var buffer = type.GetProperty("Buffer").GetValue(memory);
            Assert.That(((System.Collections.IList)buffer.GetType().GetField("acknowledgedHistory").GetValue(buffer)).Count, Is.EqualTo(1));
            type.GetMethod("ApplyAcceptedState").Invoke(memory, new[] { load });
            Assert.That(Trust(memory), Is.EqualTo(30), "A late acknowledgement cannot regress accepted state");
            type.GetMethod("ClearForCampaign").Invoke(memory, null);
            Assert.That(type.GetProperty("Relationship").GetValue(memory), Is.Null);
            Assert.That(type.GetProperty("Loaded").GetValue(memory), Is.False);
            Assert.That(type.GetProperty("Revision").GetValue(memory), Is.EqualTo(0));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void GraphReadersRequireLoadedMemoryAndExposeConfiguredCategory()
    {
        var go = new GameObject("Relationship graph"); go.SetActive(false);
        var profile = ScriptableObject.CreateInstance(Runtime("NpcProfile")); var definition = Definition();
        try
        {
            profile.GetType().GetField("relationship").SetValue(profile, definition);
            var director = go.AddComponent(Runtime("NpcCharacterDirector")); director.GetType().GetField("profile").SetValue(director, profile);
            var memory = go.GetComponent("NpcMemory"); memory.GetType().GetMethod("Awake", Hidden).Invoke(memory, null);
            var greetingType = Runtime("SelectRelationshipGreeting"); var greeting = Activator.CreateInstance(greetingType);
            greetingType.GetField("Character").SetValue(greeting, new BlackboardVariable<GameObject> { Value = go });
            greetingType.GetField("DefaultGreeting").SetValue(greeting, new BlackboardVariable<string> { Value = "Hello." });
            var output = new BlackboardVariable<string>(); greetingType.GetField("Greeting").SetValue(greeting, output);
            var readType = Runtime("ReadRelationshipCategory"); var read = Activator.CreateInstance(readType);
            readType.GetField("Character").SetValue(read, new BlackboardVariable<GameObject> { Value = go });
            var category = new BlackboardVariable<string> { Value = "trust" }; readType.GetField("Category").SetValue(read, category);
            greetingType.GetMethod("OnStart", Hidden).Invoke(greeting, null); readType.GetMethod("OnStart", Hidden).Invoke(read, null);
            Assert.That(output.Value, Is.EqualTo("Hello."));
            Assert.That(((BlackboardVariable<bool>)readType.GetField("RelationshipLoaded").GetValue(read)).Value, Is.False);
            memory.GetType().GetMethod("Apply", Hidden).Invoke(memory, new[] { Json("NpcResponse", "{\"status\":\"completed\",\"revision\":0}") });
            readType.GetMethod("OnStart", Hidden).Invoke(read, null);
            Assert.That(((BlackboardVariable<int>)readType.GetField("Value").GetValue(read)).Value, Is.EqualTo(3));
            memory.GetType().GetMethod("Apply", Hidden).Invoke(memory, new[] { Response(2, 30) });
            greetingType.GetMethod("OnStart", Hidden).Invoke(greeting, null); readType.GetMethod("OnStart", Hidden).Invoke(read, null);
            Assert.That(output.Value, Is.EqualTo("Welcome back."));
            Assert.That(((BlackboardVariable<int>)readType.GetField("Value").GetValue(read)).Value, Is.EqualTo(30));
            category.Value = "unknown"; readType.GetMethod("OnStart", Hidden).Invoke(read, null);
            Assert.That(((BlackboardVariable<bool>)readType.GetField("Found").GetValue(read)).Value, Is.False);
            var loadType = Runtime("LoadNpcMemory"); var load = Activator.CreateInstance(loadType); loadType.GetField("director", Hidden).SetValue(load, director);
            loadType.GetMethod("OnUpdate", Hidden).Invoke(load, null);
            Assert.That(((BlackboardVariable<string>)loadType.GetField("RelationshipState").GetValue(load)).Value, Does.Contain("\"category\":\"trust\"").And.Contain("\"value\":30"));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(profile); UnityEngine.Object.DestroyImmediate(definition); }
    }

    static int Trust(Component memory)
    {
        var state = memory.GetType().GetProperty("Relationship").GetValue(memory);
        foreach (var value in (Array)state.GetType().GetField("values").GetValue(state))
            if ((string)value.GetType().GetField("category").GetValue(value) == "trust") return (int)value.GetType().GetField("value").GetValue(value);
        Assert.Fail("Missing trust category"); return 0;
    }
}
