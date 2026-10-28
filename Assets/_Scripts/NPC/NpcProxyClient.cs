using System;
using System.Collections;
using System.Text;
using Core3lb;
using UnityEngine;
using UnityEngine.Networking;
using Oculus.Platform;

public class NpcProxyClient : BaseAI_LLM
{
    public string serviceUrl = "http://127.0.0.1:8787";
    [Min(1)] public int timeoutSeconds = 40;
    public bool IsPending { get; private set; }
    public NpcResponse Response { get; private set; }
    public string Error { get; private set; }
    private UnityWebRequest activeRequest;
    private Coroutine operation;
    private int generation;
    private string localToken;

    [Serializable] private class LocalSettings { public string url, token; }
    private void Awake()
    {
#if UNITY_EDITOR
        var path = System.IO.Path.Combine(UnityEngine.Application.dataPath, "../UserSettings/NpcService.local.json");
#elif UNITY_ANDROID && DEVELOPMENT_BUILD
        string path;
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var files = activity.Call<AndroidJavaObject>("getFilesDir"))
            path = System.IO.Path.Combine(files.Call<string>("getAbsolutePath"), "NpcService.local.json");
#endif
#if UNITY_EDITOR || (UNITY_ANDROID && DEVELOPMENT_BUILD)
        try
        {
            var settings = JsonUtility.FromJson<LocalSettings>(System.IO.File.ReadAllText(path));
#if UNITY_ANDROID && !UNITY_EDITOR
            if (settings == null || settings.url != "http://127.0.0.1:8787" || string.IsNullOrWhiteSpace(settings.token))
            { Error = "invalid_local_settings"; return; }
#endif
            serviceUrl = settings.url;
            localToken = settings.token;
        }
        catch (System.IO.FileNotFoundException) { }
        catch (System.IO.DirectoryNotFoundException) { }
#endif
    }
    public override void _TextPrompt(string transcript) => GetComponent<NpcCharacterDirector>()?.SubmitTranscript(transcript);
    public override void _TextPromptWithImage(string transcript, Texture2D image = null) => _TextPrompt(transcript);
    public void Submit(NpcResponseRequest request)
    {
        Cancel(); IsPending = true; Error = null; Response = null;
        operation = StartCoroutine(Send(request, generation));
    }
    public void Cancel()
    {
        generation++;
        if (activeRequest != null) { activeRequest.Abort(); activeRequest.Dispose(); activeRequest = null; }
        if (operation != null) StopCoroutine(operation);
        operation = null; IsPending = false; Response = null;
    }
    private void OnDisable() => Cancel();
    private IEnumerator Send(NpcResponseRequest body, int expected)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        if (string.IsNullOrEmpty(localToken))
        {
            bool done = Core.IsInitialized(), failed = false;
            if (!done)
            {
                try { Core.AsyncInitialize().OnComplete(m => { failed = m.IsError; done = true; }); }
                catch (UnityException) { failed = true; done = true; }
            }
            while (!done && Time.realtimeSinceStartup < deadline) yield return null;
            if (!done || failed) { Fail("platform_initialization_failed"); yield break; }
            body.identity = new NpcIdentity(); done = false;
            Users.GetLoggedInUser().OnComplete(m => { failed = m.IsError; if (!failed) body.identity.userId = m.Data.ID.ToString(); done = true; });
            while (!done && Time.realtimeSinceStartup < deadline) yield return null;
            if (!done || failed) { Fail("platform_user_failed"); yield break; }
            done = false;
            Users.GetUserProof().OnComplete(m => { failed = m.IsError; if (!failed) body.identity.nonce = m.Data.Value; done = true; });
            while (!done && Time.realtimeSinceStartup < deadline) yield return null;
            if (!done || failed) { Fail("platform_proof_failed"); yield break; }
        }
        if (expected != generation) yield break;
        activeRequest = new UnityWebRequest(serviceUrl.TrimEnd('/') + "/v1/responses", "POST");
        activeRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
        activeRequest.downloadHandler = new DownloadHandlerBuffer();
        activeRequest.SetRequestHeader("Content-Type", "application/json");
        if (!string.IsNullOrEmpty(localToken)) activeRequest.SetRequestHeader("Authorization", "Bearer " + localToken);
        activeRequest.timeout = Mathf.Max(1, Mathf.CeilToInt(deadline - Time.realtimeSinceStartup));
        yield return activeRequest.SendWebRequest();
        if (expected != generation) yield break;
        NpcResponse result = null;
        try { result = JsonUtility.FromJson<NpcResponse>(activeRequest.downloadHandler.text); }
        catch (ArgumentException) { }
        bool success = activeRequest.result == UnityWebRequest.Result.Success;
        activeRequest.Dispose(); activeRequest = null;
        if (!success || result == null || result.version != 1 || result.status != "completed" ||
            result.sessionId != body.sessionId || result.interactionId != body.interactionId || result.turnId != body.turnId || string.IsNullOrWhiteSpace(result.text))
        { Fail(result?.error ?? "invalid_or_failed_response"); yield break; }
        Response = result; IsPending = false;
        textOutput = result.text;
    }
    private void Fail(string error) { Error = error; IsPending = false; }
}
