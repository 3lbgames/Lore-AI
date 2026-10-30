using System;
using System.Collections;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class MetaAsrLiveTests
{
    [UnityTest, Explicit("Uses Meta Model API credits and a local prerecorded PCM fixture."), Category("MetaAsrLive")]
    public IEnumerator StreamsPrerecordedSpeechDirectlyFromUnity()
    {
        var key = Resources.Load<TextAsset>("MetaAsrKey.private");
        Assert.That(key, Is.Not.Null, "Create the ignored local Meta ASR credential resource.");
        string path = Path.Combine(Path.GetTempPath(), "villager-meta-asr.pcm");
        Assert.That(File.Exists(path), Is.True, "Provide 24 kHz mono PCM16 speech at " + path);
        var audio = File.ReadAllBytes(path);
        var type = Type.GetType("Core3lb.MetaAsrSession, Assembly-CSharp", true);
        using (var socket = new ClientWebSocket())
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40)))
        using (var session = (IDisposable)Activator.CreateInstance(type))
        {
            var connect = socket.ConnectAsync(new Uri("wss://api.meta.ai/v1/asr/realtime?sessionId=unity-smoke-" + Guid.NewGuid()), timeout.Token);
            while (!connect.IsCompleted) yield return null;
            Assert.That(connect.IsCompletedSuccessfully, Is.True, connect.Exception?.GetBaseException().Message);
            var run = (Task)type.GetMethod("RunAsync").Invoke(session, new object[] { socket, key.text.Trim() });
            float deadline = Time.realtimeSinceStartup + 35;
            while (!(bool)type.GetProperty("Ready").GetValue(session) && !run.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(type.GetProperty("Ready").GetValue(session), Is.True, (string)type.GetProperty("Error").GetValue(session));
            bool partialWhileStreaming = false;
            float start = Time.realtimeSinceStartup;
            for (int offset = 0; offset < audio.Length; offset += 3840)
            {
                while (Time.realtimeSinceStartup - start < offset / 48000f) yield return null;
                int count = Math.Min(3840, audio.Length - offset);
                var frame = new byte[count];
                Array.Copy(audio, offset, frame, 0, count);
                Assert.That(type.GetMethod("EnqueueAudio").Invoke(session, new object[] { frame }), Is.True,
                    (string)type.GetProperty("Error").GetValue(session));
                partialWhileStreaming |= !string.IsNullOrEmpty((string)type.GetProperty("PartialTranscript").GetValue(session));
            }
            type.GetMethod("FinishInput").Invoke(session, null);
            float ended = Time.realtimeSinceStartup;
            while (!run.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(run.IsCompleted, Is.True);
            Assert.That(type.GetProperty("Error").GetValue(session), Is.Null);
            Assert.That(partialWhileStreaming, Is.True, "Must receive provisional text before all audio is sent.");
            var transcript = (string)type.GetProperty("FinalTranscript").GetValue(session);
            Assert.That(transcript, Is.Not.Null.And.Not.Empty);
            TestContext.Out.WriteLine("Final transcript: " + transcript);
            TestContext.Out.WriteLine("Partial while streaming: " + partialWhileStreaming + "; finalization seconds: " + (Time.realtimeSinceStartup - ended));
        }
    }
}
