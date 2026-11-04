// Matches the Muse Voice PCM protocol used by MetaAsrSession in the Unity client.
export function transcribePcm(pcm, {apiKey, signal, WebSocketType = WebSocket} = {}) {
  if (!apiKey) throw Object.assign(new Error('transcription_not_configured'), {status:503});
  return new Promise((resolve, reject) => {
    const socket = new WebSocketType('wss://api.meta.ai/v1/asr/realtime?sessionId=' + crypto.randomUUID());
    let accepted = false, ended = false, finalText, offset = 0, sendTimer, settled = false;
    const finish = (error, text) => {
      if (settled) return;
      settled = true; clearTimeout(deadline); clearTimeout(sendTimer);
      signal?.removeEventListener('abort', cancelled);
      if (socket.readyState < 2) socket.close();
      if (error) reject(Object.assign(new Error(error), {status:error === 'no_speech_detected' ? 422 : 502}));
      else resolve(text);
    };
    const cancelled = () => finish('transcription_cancelled');
    const deadline = setTimeout(() => finish('transcription_timeout'), 45000);
    signal?.addEventListener('abort', cancelled, {once:true});
    if (signal?.aborted) { cancelled(); return; }
    const sendAudio = () => {
      if (settled) return;
      if (offset >= pcm.length) {
        ended = true; socket.send(JSON.stringify({type:'endStream'})); return;
      }
      const chunk = pcm.subarray(offset, offset + 4800);
      socket.send(chunk); offset += chunk.length;
      sendTimer = setTimeout(sendAudio, chunk.length / 48);
    };
    socket.onopen = () => socket.send(JSON.stringify({
      authorization:{accessToken:'Bearer ' + apiKey}, audioEncoding:'PCM_24KHZ',
      model:'muse-voice-transcribe-1.0', mode:'PUSH_TO_TALK', partialMode:'CUMULATIVE', emitAudioProgress:false,
    }));
    socket.onmessage = event => {
      try {
        if (typeof event.data !== 'string' || event.data.length > 65536) return finish('invalid_transcription_response');
        const message = JSON.parse(event.data);
        if (!accepted) {
          if (!message.sessionId || message.type) return finish('transcription_auth_failed');
          accepted = true; sendAudio(); return;
        }
        if (message.type === 'error') return finish('transcription_provider_error');
        if (message.type === 'transcript' && message.final === true) {
          if (!ended || typeof message.transcript !== 'string') return finish('invalid_transcription_response');
          finalText = message.transcript;
        }
      } catch { finish('invalid_transcription_response'); }
    };
    socket.onerror = () => finish('transcription_connection_failed');
    socket.onclose = event => {
      if (!ended || finalText === undefined || event.code !== 1000) finish('transcription_incomplete');
      else if (!finalText.trim()) finish('no_speech_detected');
      else finish(null, finalText);
    };
  });
}
