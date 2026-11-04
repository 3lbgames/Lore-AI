export function encodePcm(chunks, sampleRate) {
  const size = chunks.reduce((sum, chunk) => sum + chunk.length, 0);
  const samples = new Float32Array(size);
  let offset = 0;
  for (const chunk of chunks) { samples.set(chunk, offset); offset += chunk.length; }
  const length = Math.min(720000, Math.floor(size * 24000 / sampleRate));
  const buffer = new ArrayBuffer(length * 2), view = new DataView(buffer);
  for (let i = 0; i < length; i++) {
    const position = i * sampleRate / 24000, left = Math.floor(position), fraction = position - left;
    const sample = samples[left] * (1 - fraction) + (samples[left + 1] ?? samples[left]) * fraction;
    view.setInt16(i * 2, Math.max(-32768, Math.min(32767, Math.round(sample * 32768))), true);
  }
  return buffer;
}

export class MicrophoneRecorder {
  async start(onLimit) {
    this.chunks = []; this.cancelled = false;
    try {
      this.stream = await navigator.mediaDevices.getUserMedia({audio:{echoCancellation:true, noiseSuppression:true, channelCount:1}});
      if (this.cancelled) { this.cancel(); return; }
      this.context = new AudioContext();
      await this.context.audioWorklet.addModule('/capture-worklet.js');
      this.source = this.context.createMediaStreamSource(this.stream);
      this.capture = new AudioWorkletNode(this.context, 'microphone-capture');
      this.capture.port.onmessage = event => {
        if (event.data.samples) this.chunks.push(event.data.samples);
        if (event.data.done) this.flushed?.();
      };
      this.source.connect(this.capture); this.capture.connect(this.context.destination);
      await this.context.resume();
      this.timer = setTimeout(onLimit, 30000);
    } catch (error) { this.cancel(); throw error; }
  }
  async stop() {
    clearTimeout(this.timer);
    const rate = this.context.sampleRate;
    try {
      await new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error('Microphone capture did not finish. Please try again.')), 1500);
        this.flushed = () => { clearTimeout(timer); resolve(); };
        this.capture.port.postMessage('finish');
      });
      return encodePcm(this.chunks, rate);
    } finally { this.cancel(); }
  }
  cancel() {
    this.cancelled = true; clearTimeout(this.timer);
    this.stream?.getTracks().forEach(track => track.stop());
    this.source?.disconnect(); this.capture?.disconnect();
    if (this.capture) { this.capture.port.onmessage = null; this.capture.port.close(); }
    if (this.context?.state !== 'closed') this.context?.close().catch(() => {});
    this.chunks = [];
  }
}
