using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

public class MetaAsrCapturePlayTests
{
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator DisabledAndMissingCredentialFailuresLeaveCaptureIdle()
    {
        var owner = new GameObject("Meta ASR lifecycle");
        try
        {
            var type = Type.GetType("Core3lb.MetaStreamingSpeechToText, Assembly-CSharp", true);
            var provider = (Behaviour)owner.AddComponent(type);
            var failed = new UnityEvent<string>();
            type.GetField("aiFailed").SetValue(provider, failed);
            string error = null;
            int failures = 0;
            failed.AddListener(value => { error = value; failures++; });
            provider.enabled = false;
            type.GetMethod("_StartListening").Invoke(provider, null);
            Assert.That(error, Is.EqualTo("transcription_disabled"));
            provider.enabled = true;
            type.GetField("credentialResource").SetValue(provider, "NoSuchAsrTestCredential");
            type.GetMethod("SetAPIKey").Invoke(provider, new object[] { "" });
            type.GetMethod("_StartListening").Invoke(provider, null);
            yield return null;
            Assert.That(error, Is.EqualTo("missing_meta_asr_key"));
            Assert.That(failures, Is.EqualTo(2));
            Assert.That(type.GetProperty("IsRecording").GetValue(provider), Is.False);
            Assert.That(type.GetProperty("IsProcessing").GetValue(provider), Is.False);
            Assert.That(type.GetProperty("PartialTranscript").GetValue(provider), Is.Null);
        }
        finally { UnityEngine.Object.Destroy(owner); }
    }
}
