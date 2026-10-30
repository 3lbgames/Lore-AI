using System;
using System.Collections;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Core3lb
{
    public class MetaStreamingSpeechToText : BaseAI_SpeechToText, ISpeechCaptureStatus
    {
        public string credentialResource = "MetaAsrKey.private";
        private const int FrameSamples = MetaAsrSession.SampleRate * 80 / 1000;
        private MetaAsrSession session;
        private ClientWebSocket connectingSocket;
        private CancellationTokenSource connecting;
        private AudioClip microphone;
        private float[] frameBuffer;
        private string device;
        private int readPosition, generation;
        private long capturedSamples;
        private bool released, capturing;
        private float lastReadTime, startedAt, releasedAt;
        public bool IsRecording => microphone && !released && Microphone.GetPosition(device) > 0;
        public bool IsProcessing => session != null && released;
        public string PartialTranscript => session?.PartialTranscript;
        public float FinalizationSeconds { get; private set; }
        public string LastError { get; private set; }
        public override void Start() { }

        public override void _StartListening()
        {
            _CancelListening();
            if (!isActiveAndEnabled) { Fail("transcription_disabled"); return; }
            var resource = Resources.Load<TextAsset>(credentialResource);
            string key = resource ? resource.text.Trim() : useStringAPIKey ? apiKeyString : apiKeySO ? apiKeySO.GetString : null;
            if (string.IsNullOrWhiteSpace(key)) { Fail("missing_meta_asr_key"); return; }
            LastError = null;
            FinalizationSeconds = 0;
            released = false;
            capturing = true;
            startedAt = Time.realtimeSinceStartup;
            Debug.Log("Meta ASR: starting capture", this);
            StartCoroutine(Capture(generation, key));
        }

        private IEnumerator Capture(int expected, string key)
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                float permissionDeadline = Time.realtimeSinceStartup + 20;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone) &&
                       Time.realtimeSinceStartup < permissionDeadline && expected == generation && !released) yield return null;
                if (expected != generation) yield break;
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                { Fail("microphone_permission_denied"); yield break; }
            }
    #else
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
                yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
    #endif
            if (expected != generation) yield break;
            if (released) { Fail("capture_released_before_ready"); yield break; }
            if (Microphone.devices.Length == 0) { Fail("microphone_unavailable"); yield break; }
            session = new MetaAsrSession();
            device = forceInputDevice ? inputDevice : null;
            string captureError = null;
            try { microphone = Microphone.Start(device, true, 4, MetaAsrSession.SampleRate); }
            catch (Exception) { captureError = "microphone_unavailable"; }
            if (captureError != null || !microphone) { Fail(captureError ?? "microphone_unavailable"); yield break; }
            if (microphone.frequency != MetaAsrSession.SampleRate) { Fail("unsupported_microphone_sample_rate"); yield break; }
            Debug.Log("Meta ASR: microphone " + microphone.frequency + "Hz, " + microphone.channels + " channel(s)", this);
            frameBuffer = new float[FrameSamples * microphone.channels];
            readPosition = 0;
            capturedSamples = 0;
            lastReadTime = Time.realtimeSinceStartup;
            startedListening?.Invoke();
            float readyDeadline = Time.realtimeSinceStartup + 10;
            connecting = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            connectingSocket = new ClientWebSocket();
            Task connect = connectingSocket.ConnectAsync(new Uri("wss://api.meta.ai/v1/asr/realtime?sessionId=" + Guid.NewGuid()), connecting.Token);
            while (!connect.IsCompleted && expected == generation)
            {
                if (!released && !ReadAudio(false)) yield break;
                yield return null;
            }
            if (expected != generation) yield break;
            if (connect.IsCanceled || connect.IsFaulted)
            { _ = connect.Exception; Fail("transcription_connection_failed"); yield break; }
            var current = session;
            var run = current.RunAsync(connectingSocket, key);
            connectingSocket = null;
            connecting.Dispose(); connecting = null;
            while (!current.Ready && !run.IsCompleted && expected == generation && Time.realtimeSinceStartup < readyDeadline)
            {
                if (!released && !ReadAudio(false)) yield break;
                yield return null;
            }
            if (expected != generation) yield break;
            if (!current.Ready && !run.IsCompleted) { Fail("transcription_start_failed"); yield break; }
            Debug.Log("Meta ASR: provider startup completed after " + (Time.realtimeSinceStartup - startedAt).ToString("F2") + "s", this);
            while (!run.IsCompleted && expected == generation)
            {
                if (Time.realtimeSinceStartup - startedAt > 38) { Fail("capture_timeout"); yield break; }
                if (!released)
                {
                    if (!ReadAudio(false)) yield break;
                    if (Time.realtimeSinceStartup - lastReadTime > 3) { Fail("microphone_stalled"); yield break; }
                }
                yield return null;
            }
            if (expected != generation) yield break;
            string error = current.Error;
            string transcript = current.FinalTranscript;
            FinalizationSeconds = released ? Time.realtimeSinceStartup - releasedAt : 0;
            _CancelListening();
            if (error != null || string.IsNullOrWhiteSpace(transcript)) Fail(error ?? "transcription_incomplete");
            else AISuccess(transcript);
        }

        private bool ReadAudio(bool flush)
        {
            if (!microphone) return true;
            if (Time.realtimeSinceStartup - lastReadTime >= 4) { Fail("microphone_buffer_overrun"); return false; }
            int position = Microphone.GetPosition(device);
            if (position < 0 || !Microphone.IsRecording(device)) { Fail("microphone_unavailable"); return false; }
            int available = (position - readPosition + microphone.samples) % microphone.samples;
            while (available >= FrameSamples || (flush && available > 0))
            {
                int count = Math.Min(FrameSamples, available);
                var samples = count == FrameSamples ? frameBuffer : new float[count * microphone.channels];
                if (!microphone.GetData(samples, readPosition)) { Fail("microphone_read_failed"); return false; }
                if (!session.EnqueueAudio(MetaAsrSession.EncodePcm(samples, microphone.channels)))
                { Fail(session.Error ?? "audio_send_failed"); return false; }
                if (capturedSamples == 0)
                    Debug.Log("Meta ASR: first microphone audio after " + (Time.realtimeSinceStartup - startedAt).ToString("F2") + "s", this);
                capturedSamples += count;
                readPosition = (readPosition + count) % microphone.samples;
                available -= count;
                lastReadTime = Time.realtimeSinceStartup;
            }
            return true;
        }

        public override void _StopRecordingAndProcess()
        {
            if (released) return;
            if (microphone && !ReadAudio(true)) return;
            released = true;
            releasedAt = Time.realtimeSinceStartup;
            Debug.Log("Meta ASR: released after " + (releasedAt - startedAt).ToString("F2") + "s; captured " + capturedSamples + "; sent " + (session?.SentSamples ?? 0) + " samples", this);
            StopMicrophone();
            session?.FinishInput();
        }

        public override void _CancelListening()
        {
            generation++;
            capturing = false;
            StopAllCoroutines();
            connecting?.Cancel(); connecting?.Dispose(); connecting = null;
            connectingSocket?.Abort(); connectingSocket?.Dispose(); connectingSocket = null;
            session?.Dispose(); session = null;
            StopMicrophone();
            released = false;
        }

        private void StopMicrophone()
        {
            if (!microphone) return;
            Microphone.End(device);
            Destroy(microphone);
            microphone = null;
            stopListening?.Invoke();
        }

        private void Fail(string reason)
        {
            _CancelListening();
            LastError = reason;
            Debug.LogWarning("Meta ASR capture failed: " + reason, this);
            aiFailed?.Invoke(reason);
        }
        private void OnDisable() { if (capturing) Fail("capture_disabled"); else _CancelListening(); }
        private void OnApplicationPause(bool paused) { if (paused && capturing) Fail("capture_paused"); }
    }

}
