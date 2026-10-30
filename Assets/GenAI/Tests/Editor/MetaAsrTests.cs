using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;
using System.Linq;

public class MetaAsrTests
{
    private Type Type => System.Type.GetType("Core3lb.MetaAsrSession, Assembly-CSharp", true);

    [TestCase("Assets/_Scenes/BaseScene.unity")]
    [TestCase("Assets/_Scenes/VillageScene.unity")]
    public void ShippedSceneUsesExactlyOneMetaCapture(string path)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene(path);
        try
        {
            var components = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true)).Where(c => c).ToArray();
            var input = components.Single(c => c.GetType().Name == "NpcSpeechInput");
            var provider = new SerializedObject(input).FindProperty("transcription").objectReferenceValue;
            Assert.That(provider.GetType().Name, Is.EqualTo("MetaStreamingSpeechToText"));
            Assert.That(components.Count(c => c.GetType().Name == "MetaStreamingSpeechToText"), Is.EqualTo(1));
            Assert.That(input.GetComponents<MonoBehaviour>().Any(c => c.GetType().Name == "AppVoiceExperience"), Is.False);
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test] public void PlayerPrefabUsesMetaWithoutAnActiveWitMicrophone()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prefabs/NpcSpeechInput.prefab");
        var components = prefab.GetComponents<MonoBehaviour>();
        var input = components.Single(c => c.GetType().Name == "NpcSpeechInput");
        var provider = new SerializedObject(input).FindProperty("transcription").objectReferenceValue;
        Assert.That(provider.GetType().Name, Is.EqualTo("MetaStreamingSpeechToText"));
        Assert.That(components.Any(c => c.GetType().Name == "AppVoiceExperience"), Is.False);
        Assert.That(new SerializedObject(provider).FindProperty("apiKeyString").stringValue, Is.Empty);
    }

    [UnityTest] public IEnumerator PartialsReplaceAndOnlyExactFinalSurvivesDuplicateEvents()
    {
        using (var socket = new ScriptedSocket())
        using (var session = (IDisposable)Activator.CreateInstance(Type))
        {
            var task = Run(session, socket);
            socket.Push("{\"type\":\"transcript\",\"transcript\":\"I want a sword\",\"final\":false}");
            yield return WaitFor(() => (string)Get(session, "PartialTranscript") == "I want a sword");
            socket.Push("{\"type\":\"transcript\",\"transcript\":\"I want an apple\",\"final\":false}");
            yield return WaitFor(() => (string)Get(session, "PartialTranscript") == "I want an apple");
            Assert.That(Get(session, "FinalTranscript"), Is.Null);
            Type.GetMethod("EnqueueAudio").Invoke(session, new object[] { new byte[3840] });
            Type.GetMethod("FinishInput").Invoke(session, null);
            yield return WaitFor(() => task.IsCompleted);
            Assert.That(Get(session, "Error"), Is.Null);
            Assert.That(Get(session, "FinalTranscript"), Is.EqualTo("  An apple, please!  "));
            Assert.That(socket.Sent[0], Does.Contain("\"accessToken\":\"Bearer test-key\""));
            Assert.That(socket.Sent[0], Does.Contain("\"mode\":\"PUSH_TO_TALK\""));
            Assert.That(socket.BinaryBytes, Is.EqualTo(3840));
            Assert.That(socket.Sent.Last(), Is.EqualTo("{\"type\":\"endStream\"}"));
        }
    }

    [UnityTest] public IEnumerator DisconnectAndProviderErrorsNeverPromotePartialText()
    {
        foreach (string scenario in new[] { "disconnect", "error", "empty", "oversize", "malformed" })
        using (var socket = new ScriptedSocket { Scenario = scenario })
        using (var session = (IDisposable)Activator.CreateInstance(Type))
        {
            var task = Run(session, socket);
            socket.Push("{\"type\":\"transcript\",\"transcript\":\"uncommitted words\",\"final\":false}");
            yield return WaitFor(() => (string)Get(session, "PartialTranscript") == "uncommitted words");
            Type.GetMethod("FinishInput").Invoke(session, null);
            yield return WaitFor(() => task.IsCompleted);
            Assert.That(Get(session, "Error"), Is.Not.Null, scenario);
            Assert.That(Get(session, "FinalTranscript"), Is.Null, scenario);
        }
    }

    [UnityTest] public IEnumerator CancellingSessionDoesNotLeakItsResultIntoTheNextCapture()
    {
        using (var oldSocket = new ScriptedSocket())
        using (var old = (IDisposable)Activator.CreateInstance(Type))
        {
            var task = Run(old, oldSocket);
            old.Dispose();
            yield return WaitFor(() => task.IsCompleted);
            oldSocket.Push("{\"type\":\"transcript\",\"transcript\":\"obsolete\",\"final\":true}");
            Assert.That(Get(old, "FinalTranscript"), Is.Null);
            using (var socket = new ScriptedSocket())
            using (var next = (IDisposable)Activator.CreateInstance(Type))
            {
                Type.GetMethod("FinishInput").Invoke(next, null);
                var nextTask = Run(next, socket);
                yield return WaitFor(() => nextTask.IsCompleted);
                Assert.That(Get(next, "Error"), Is.Null);
                Assert.That(Get(next, "FinalTranscript"), Is.EqualTo("  An apple, please!  "));
            }
        }
    }

    private object Get(object session, string property) => Type.GetProperty(property).GetValue(session);
    private Task Run(object session, WebSocket socket) => (Task)Type.GetMethod("RunAsync").Invoke(session, new object[] { socket, "test-key" });
    private static IEnumerator WaitFor(Func<bool> predicate)
    {
        float deadline = Time.realtimeSinceStartup + 4;
        while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(predicate(), Is.True, "Timed out waiting for protocol progress");
    }

    // Exercise the real session against WebSocket frames, including fragmented UTF-8 messages.
    private sealed class ScriptedSocket : WebSocket
    {
        readonly Queue<byte[]> incoming = new Queue<byte[]>();
        int offset;
        bool close, aborted;
        public string Scenario = "success";
        public readonly List<string> Sent = new List<string>();
        public int BinaryBytes;
        public ScriptedSocket() => Push("{\"sessionId\":\"test-session\"}");
        public void Push(string message) => incoming.Enqueue(Encoding.UTF8.GetBytes(message));
        public override WebSocketCloseStatus? CloseStatus => close ? WebSocketCloseStatus.NormalClosure : (WebSocketCloseStatus?)null;
        public override string CloseStatusDescription => null;
        public override string SubProtocol => null;
        public override WebSocketState State => aborted ? WebSocketState.Aborted : close ? WebSocketState.CloseReceived : WebSocketState.Open;
        public override void Abort() => aborted = true;
        public override void Dispose() => aborted = true;
        public override Task CloseAsync(WebSocketCloseStatus status, string description, CancellationToken token) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string description, CancellationToken token) => Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (type == WebSocketMessageType.Binary) { BinaryBytes += buffer.Count; return Task.CompletedTask; }
            var text = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count);
            Sent.Add(text);
            if (text.Contains("endStream"))
            {
                Push("{\"type\":\"futureEvent\"}");
                if (Scenario == "error") Push("{\"type\":\"error\",\"message\":\"failure\"}");
                else if (Scenario == "empty") Push("{\"type\":\"transcript\",\"transcript\":\"\",\"final\":true}");
                else if (Scenario == "oversize") Push(new string('x', 65537));
                else if (Scenario == "malformed") Push("not JSON");
                else if (Scenario != "disconnect")
                {
                    Push("{\"type\":\"transcript\",\"transcript\":\"  An apple, please!  \",\"final\":true}");
                    Push("{\"type\":\"transcript\",\"transcript\":\"duplicate\",\"final\":true}");
                }
                close = true;
            }
            return Task.CompletedTask;
        }
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
        {
            while (incoming.Count == 0 && !close) await Task.Delay(1, token);
            token.ThrowIfCancellationRequested();
            if (incoming.Count == 0) return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
            var message = incoming.Peek();
            int count = Math.Min(Math.Min(buffer.Count, 1024), message.Length - offset);
            Array.Copy(message, offset, buffer.Array, buffer.Offset, count);
            offset += count;
            bool end = offset == message.Length;
            if (end) { incoming.Dequeue(); offset = 0; }
            return new WebSocketReceiveResult(count, WebSocketMessageType.Text, end);
        }
    }

    [Test] public void PcmIsMonoSignedLittleEndianAndClipsInsteadOfWrapping()
    {
        var samples = new[] { -2f, -2f, 2f, 2f, .5f, -.5f, .5f, .5f };
        var result = (byte[])Type.GetMethod("EncodePcm").Invoke(null, new object[] { samples, 2 });
        Assert.That(result, Is.EqualTo(new byte[] { 0, 128, 255, 127, 0, 0, 0, 64 }));
    }

    [UnityTest] public IEnumerator OpeningAudioReleasedBeforeConnectionIsPreserved()
    {
        using (var socket = new ScriptedSocket())
        using (var session = (IDisposable)Activator.CreateInstance(Type))
        {
            // A short utterance can finish while the network is still connecting.
            var pcm = new byte[24000 * 2 * 3];
            Assert.That(Type.GetMethod("EnqueueAudio").Invoke(session, new object[] { pcm }), Is.True);
            Type.GetMethod("FinishInput").Invoke(session, null);
            var task = Run(session, socket);
            yield return WaitFor(() => task.IsCompleted);
            Assert.That(Get(session, "Error"), Is.Null);
            Assert.That(socket.BinaryBytes, Is.EqualTo(pcm.Length));
            Assert.That(socket.Sent.Last(), Is.EqualTo("{\"type\":\"endStream\"}"));
            Assert.That(Get(session, "FinalTranscript"), Is.EqualTo("  An apple, please!  "));
        }
    }

    [Test] public void QueuedMicrophoneAudioIsBoundedAndOverflowIsExplicit()
    {
        var session = Activator.CreateInstance(Type);
        try
        {
            var enqueue = Type.GetMethod("EnqueueAudio");
            Assert.That(enqueue.Invoke(session, new object[] { new byte[24000 * 2 * 12] }), Is.True);
            Assert.That(enqueue.Invoke(session, new object[] { new byte[2] }), Is.False);
            Assert.That(Type.GetProperty("Error").GetValue(session), Is.EqualTo("audio_backlog_overflow"));
        }
        finally { ((IDisposable)session).Dispose(); }
    }
}
