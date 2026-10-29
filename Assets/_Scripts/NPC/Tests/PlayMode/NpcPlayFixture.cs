#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Behavior;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Native PlayMode fixtures share the runner's play session, but own separate scenes and services.
internal sealed class NpcPlayFixture
{
    const string PrefabPath = "Assets/_Prefabs/NPCs/Barbarian_NPC Variant.prefab";
    readonly Scene scene, previousScene;
    readonly float volume = AudioListener.volume;
    readonly bool background = Application.runInBackground;
    readonly bool live;
    public readonly NpcTestService Service;

    public NpcPlayFixture(bool useLiveProviders = false)
    {
        Assert.That(Application.isPlaying, Is.True, "Run NpcInteraction.PlayModeTests in PlayMode");
        live = useLiveProviders;
        previousScene = SceneManager.GetActiveScene();
        scene = SceneManager.CreateScene("NPC test " + Guid.NewGuid().ToString("N"));
        SceneManager.SetActiveScene(scene);
        if (!live) Service = new NpcTestService();
        AudioListener.volume = 0; Application.runInBackground = true;
    }

    public GameObject InstantiateNpc(string prefabPath = null)
    {
        var holder = new GameObject("Inactive prefab staging");
        holder.SetActive(false);
        var npc = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath ?? PrefabPath), holder.transform);
        npc.SetActive(false); npc.transform.SetParent(null);
        UnityEngine.Object.Destroy(holder);
        if (!live)
        {
            var speaker = new GameObject("Controlled speech");
            speaker.transform.SetParent(npc.transform);
            var speech = speaker.AddComponent(Type.GetType("NpcTestSpeech, Assembly-CSharp", true));
            var output = speaker.AddComponent(Type.GetType("NpcSpeechOutput, Assembly-CSharp", true));
            var caption = npc.GetComponentInChildren(Type.GetType("NpcSpeechCaptionView, Assembly-CSharp", true), true);
            if (caption) caption.GetType().GetField("output").SetValue(caption, output);
            var director = npc.GetComponent("NpcCharacterDirector");
            director.GetType().GetField("speech").SetValue(director, speech);
            var agent = npc.GetComponent<BehaviorGraphAgent>();
            // Set the prefab override before Init, which reapplies overrides on activation.
            Assert.That(agent.SetVariableValue("Speaker", speaker), Is.True);
        }
        return npc;
    }

    public void Activate(GameObject npc)
    {
        npc.SetActive(true); // Awake loads local settings; override them before the first Update.
        if (Service != null) Service.Configure(npc);
    }

    public IEnumerator Dispose()
    {
        foreach (var root in scene.GetRootGameObjects()) root.SetActive(false);
        Service?.Dispose();
        SceneManager.SetActiveScene(previousScene);
        yield return SceneManager.UnloadSceneAsync(scene);
        AudioListener.volume = volume; Application.runInBackground = background;
    }
}

internal sealed class NpcTestService : IDisposable
{
    readonly HttpListener listener = new HttpListener();
    readonly List<HttpListenerContext> held = new List<HttpListenerContext>();
    readonly Dictionary<string, List<MemoryEvent>> histories = new Dictionary<string, List<MemoryEvent>>();
    readonly Task worker;
    public volatile bool HoldPlayerResponses;
    public readonly string Url;
    public NpcTestService()
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start(); int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        Url = "http://127.0.0.1:" + port;
        listener.Prefixes.Add(Url + "/"); listener.Start();
        worker = Task.Run(Serve);
    }
    public void Configure(GameObject npc)
    {
        var proxy = npc.GetComponent("NpcProxyClient");
        proxy.GetType().GetField("serviceUrl").SetValue(proxy, Url);
        proxy.GetType().GetField("timeoutSeconds").SetValue(proxy, 5);
        proxy.GetType().GetField("localToken", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proxy, "test-token");
    }
    async Task Serve()
    {
        try
        {
            while (listener.IsListening)
            {
                var context = await listener.GetContextAsync();
                var body = JsonUtility.FromJson<Envelope>(await new StreamReader(context.Request.InputStream).ReadToEndAsync());
                string route = context.Request.Url.AbsolutePath;
                // Openings must complete so the graph can reach listening. Only player replies are held.
                if (route == "/v1/responses" && !body.npcInitiated && HoldPlayerResponses)
                { lock (held) held.Add(context); continue; }
                string key = body.worldId + "/" + body.campaignId + "/" + body.npcId;
                if (!histories.TryGetValue(key, out var history)) histories[key] = history = new List<MemoryEvent>();
                if (route == "/v1/memory/commit" && !history.Exists(e => e.eventId == body.@event.eventId))
                {
                    if (body.expectedRevision != history.Count)
                    {
                        context.Response.StatusCode = 409;
                        body.status = "failed"; body.error = "revision_conflict";
                    }
                    else history.Add(body.@event);
                }
                body.revision = history.Count; body.history = history.ToArray();
                body.hasSpoken = history.Exists(e => e.kind == "player_input");
                if (route == "/v1/responses") body.text = body.npcInitiated ? "Hello, traveler." : "The inn is east of here.";
                byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                context.Response.Close();
            }
        }
        catch (HttpListenerException) when (!listener.IsListening) { }
        catch (ObjectDisposedException) { }
    }
    public void Dispose()
    {
        listener.Stop();
        lock (held) foreach (var context in held) context.Response.Abort();
        listener.Close();
        Assert.That(worker.Wait(TimeSpan.FromSeconds(2)), Is.True, "Fake service shuts down without leaking requests");
    }
    [Serializable] class MemoryEvent { public string eventId, turnId, kind, text, delivery; }
    [Serializable] class Envelope
    {
        public int version = 1, revision, expectedRevision;
        public string sessionId, interactionId, turnId, worldId, campaignId, npcId, text, error, status = "completed";
        public bool hasSpoken, npcInitiated;
        public MemoryEvent @event;
        public MemoryEvent[] history;
    }
}
#endif
