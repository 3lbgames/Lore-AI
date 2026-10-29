using System;
using NUnit.Framework;
using UnityEngine;

public class NpcActionTests
{
    [Test]
    public void TurnTransportCarriesOptionalSpeechAndConcreteActionTarget()
    {
        var type = Type.GetType("NpcResponse, Assembly-CSharp", true);
        var response = JsonUtility.FromJson("{\"text\":\"\",\"actionProposal\":{\"actionId\":\"wave\",\"targetId\":\"self\"}}", type);
        var field = type.GetField("actionProposal");
        Assert.That(field, Is.Not.Null, "Structured action must survive the service transport independently of speech");
        var action = field.GetValue(response);
        Assert.That(action.GetType().GetField("actionId").GetValue(action), Is.EqualTo("wave"));
        Assert.That(action.GetType().GetField("targetId").GetValue(action), Is.EqualTo("self"));
    }
}
