class MicrophoneCapture extends AudioWorkletProcessor {
  constructor() {
    super(); this.buffer = new Float32Array(2048); this.used = 0; this.finished = false;
    this.port.onmessage = () => { this.flush(); this.finished = true; this.port.postMessage({done:true}); };
  }
  flush() {
    if (this.used) this.port.postMessage({samples:this.buffer.slice(0, this.used)});
    this.used = 0;
  }
  process(inputs) {
    if (this.finished) return false;
    const channels = inputs[0];
    if (!channels?.length) return true;
    for (let i = 0; i < channels[0].length; i++) {
      let sample = 0;
      for (const channel of channels) sample += channel[i];
      this.buffer[this.used++] = sample / channels.length;
      if (this.used === this.buffer.length) this.flush();
    }
    return true;
  }
}
registerProcessor('microphone-capture', MicrophoneCapture);
