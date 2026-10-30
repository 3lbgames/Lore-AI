using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Core3lb
{
    public sealed class MetaAsrSession : IDisposable
    {
        public const int SampleRate = 24000;
        private const int MaxBufferedAudioSeconds = 12;
        private const int MaxQueuedBytes = SampleRate * 2 * MaxBufferedAudioSeconds;
        private readonly Queue<byte[]> audio = new Queue<byte[]>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private WebSocket socket;
        private int queuedBytes;
        private bool inputEnded, endSent, cancelled, running;
        public bool Ready { get; private set; }
        public bool Complete { get; private set; }
        public string PartialTranscript { get; private set; }
        public string FinalTranscript { get; private set; }
        public string Error { get; private set; }
        public long SentSamples { get; private set; }

        public bool EnqueueAudio(byte[] pcm)
        {
            if (cancelled || Complete || inputEnded || Error != null) return false;
            if (pcm.Length % 2 != 0) throw new ArgumentException("PCM must contain complete samples.");
            if (queuedBytes + pcm.Length > MaxQueuedBytes)
            {
                Fail("audio_backlog_overflow");
                return false;
            }
            audio.Enqueue(pcm);
            queuedBytes += pcm.Length;
            return true;
        }

        public void FinishInput() => inputEnded = true;

        // All access is on Unity's synchronization context; continuations never mutate Unity from workers.
        public async Task RunAsync(WebSocket connectedSocket, string key)
        {
            if (cancelled || running || Complete) throw new InvalidOperationException("Session cannot be reused.");
            running = true;
            socket = connectedSocket;
            Task sender = null;
            lifetime.CancelAfter(TimeSpan.FromSeconds(40));
            try
            {
                await SendText(new JObject {
                    ["authorization"] = new JObject { ["accessToken"] = "Bearer " + key },
                    ["audioEncoding"] = "PCM_24KHZ", ["model"] = "muse-voice-transcribe-1.0",
                    ["mode"] = "PUSH_TO_TALK", ["partialMode"] = "CUMULATIVE",
                    ["emitAudioProgress"] = false
                }.ToString(Newtonsoft.Json.Formatting.None));
                var acknowledgement = await ReadMessage();
                if (acknowledgement == null || string.IsNullOrEmpty((string)acknowledgement["sessionId"]) || acknowledgement["type"] != null)
                    throw new InvalidDataException("handshake_rejected");
                Ready = true;
                sender = SendAudio();
                while (true)
                {
                    var message = await ReadMessage();
                    if (message == null)
                    {
                        if (!endSent || FinalTranscript == null || socket.CloseStatus != WebSocketCloseStatus.NormalClosure)
                            throw new InvalidDataException("transcription_incomplete");
                        break;
                    }
                    if ((string)message["type"] == "error") throw new InvalidDataException("provider_error");
                    if ((string)message["type"] != "transcript" || FinalTranscript != null) continue;
                    if (message["transcript"]?.Type != JTokenType.String)
                        throw new InvalidDataException("invalid_transcript");
                    var text = (string)message["transcript"];
                    if ((bool?)message["final"] == true)
                    {
                        if (!endSent) throw new InvalidDataException("unexpected_final");
                        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("empty_transcript");
                        FinalTranscript = text;
                    }
                    else PartialTranscript = text;
                }
                await sender;
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", lifetime.Token);
            }
            catch (OperationCanceledException) { if (!cancelled && Error == null) Error = "transcription_timeout"; }
            catch (InvalidDataException e) { if (!cancelled && Error == null) Error = e.Message; }
            catch (Exception) { if (!cancelled && Error == null) Error = "transcription_connection_failed"; }
            finally
            {
                lifetime.Cancel();
                socket.Abort();
                if (sender != null) { try { await sender; } catch (Exception) { } }
                socket.Dispose();
                socket = null;
                Ready = false;
                Complete = true;
                running = false;
                audio.Clear();
                queuedBytes = 0;
                lifetime.Dispose();
            }
        }

        private async Task SendAudio()
        {
            var clock = Stopwatch.StartNew();
            double nextSend = 0;
            try
            {
                while (true)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    if (audio.Count > 0)
                    {
                        double delay = nextSend - clock.Elapsed.TotalSeconds;
                        if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay), lifetime.Token);
                        var bytes = audio.Dequeue();
                        queuedBytes -= bytes.Length;
                        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, lifetime.Token);
                        SentSamples += bytes.Length / 2;
                        // Retain the clock across frames without bursting after a stalled network send.
                        nextSend = Math.Max(nextSend, clock.Elapsed.TotalSeconds - .02) + bytes.Length / (SampleRate * 2.0);
                    }
                    else if (inputEnded)
                    {
                        endSent = true;
                        await SendText("{\"type\":\"endStream\"}");
                        return;
                    }
                    else await Task.Delay(5, lifetime.Token);
                }
            }
            catch (Exception)
            {
                if (!cancelled && Error == null) Error = "audio_send_failed";
                lifetime.Cancel();
                socket.Abort();
                throw;
            }
        }

        private Task SendText(string text) => socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)),
            WebSocketMessageType.Text, true, lifetime.Token);

        private async Task<JObject> ReadMessage()
        {
            var buffer = new byte[4096];
            using (var message = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), lifetime.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return null;
                    if (result.MessageType != WebSocketMessageType.Text || message.Length + result.Count > 65536)
                        throw new InvalidDataException("invalid_provider_message");
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                return JObject.Parse(Encoding.UTF8.GetString(message.ToArray()));
            }
        }

        private void Fail(string reason)
        {
            Error = reason;
            lifetime.Cancel();
            socket?.Abort();
        }

        public void Dispose()
        {
            if (cancelled) return;
            cancelled = true;
            if (!Complete) lifetime.Cancel();
            socket?.Abort();
            audio.Clear();
            queuedBytes = 0;
            PartialTranscript = null;
            if (!running && !Complete) lifetime.Dispose();
        }

        public static byte[] EncodePcm(float[] interleaved, int channels)
        {
            if (channels < 1 || interleaved.Length % channels != 0) throw new ArgumentException("Invalid channel count.");
            var bytes = new byte[interleaved.Length / channels * 2];
            for (int frame = 0; frame < bytes.Length / 2; frame++)
            {
                double mono = 0;
                for (int channel = 0; channel < channels; channel++) mono += interleaved[frame * channels + channel];
                mono /= channels;
                short sample = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(mono * 32768)));
                bytes[frame * 2] = (byte)(sample & 255);
                bytes[frame * 2 + 1] = (byte)((sample >> 8) & 255);
            }
            return bytes;
        }
    }

}
