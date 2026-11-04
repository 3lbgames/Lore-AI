import test from 'node:test';
import assert from 'node:assert/strict';
import { encodePcm } from '../demo/dictation.js';
import { transcribePcm } from '../demo/transcribe.js';

test('microphone samples become clipped mono 24 kHz signed little-endian PCM', () => {
  const pcm = encodePcm([new Float32Array([0, 0.1, 1, 0.1]), new Float32Array([-1, 0.1, 2, 0.1])], 48000);
  const view = new DataView(pcm);
  assert.equal(pcm.byteLength, 8);
  assert.deepEqual([0,2,4,6].map(i => view.getInt16(i, true)), [0,32767,-32768,32767]);
  assert.equal(encodePcm([new Float32Array(24000 * 31)], 24000).byteLength, 1440000);
});

test('Meta dictation authenticates server-side, sends PCM then endStream, and requires finalized text', async () => {
  const sent = [];
  class Socket {
    readyState = 1;
    constructor() { queueMicrotask(() => this.onopen()); }
    close() { this.readyState = 3; }
    send(value) {
      sent.push(value);
      if (typeof value !== 'string') return;
      const message = JSON.parse(value);
      if (message.authorization) queueMicrotask(() => this.onmessage({data:JSON.stringify({sessionId:'test'})}));
      if (message.type === 'endStream') queueMicrotask(() => {
        this.onmessage({data:JSON.stringify({type:'transcript', final:true, transcript:'Move the wasps gently.'})});
        this.onclose({code:1000});
      });
    }
  }
  const pcm = Buffer.from([0, 1]);
  assert.equal(await transcribePcm(pcm, {apiKey:'test-key', WebSocketType:Socket}), 'Move the wasps gently.');
  assert.equal(JSON.parse(sent[0]).authorization.accessToken, 'Bearer test-key');
  assert.equal(JSON.parse(sent[0]).audioEncoding, 'PCM_24KHZ');
  assert.deepEqual(sent[1], pcm);
  assert.deepEqual(JSON.parse(sent[2]), {type:'endStream'});
});

test('provider errors cannot leak raw messages or credentials to the page', async () => {
  class Socket {
    readyState = 1;
    constructor() { queueMicrotask(() => this.onopen()); }
    close() { this.readyState = 3; }
    send() { queueMicrotask(() => this.onmessage({data:JSON.stringify({type:'error',message:'secret-provider-detail'})})); }
  }
  await assert.rejects(transcribePcm(Buffer.from([0,1]), {apiKey:'private-key',WebSocketType:Socket}), {message:'transcription_auth_failed'});
});
