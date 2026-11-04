import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { transcribePcm } from './transcribe.js';

const files = new Map([
  ['/', ['index.html', 'text/html']], ['/app.js', ['app.js', 'text/javascript']],
  ['/client.js', ['client.js', 'text/javascript']], ['/profiles.js', ['profiles.js', 'text/javascript']],
  ['/style.css', ['style.css', 'text/css']],
  ['/dictation.js', ['dictation.js', 'text/javascript']],
  ['/capture-worklet.js', ['capture-worklet.js', 'text/javascript']],
]);
const routes = new Set(['/v1/memory/load', '/v1/memory/commit', '/v1/game/load', '/v1/game/commit', '/v1/responses']);

export function createDemoServer({upstream = 'http://127.0.0.1:8787', token, apiKey,
  transcribe = (pcm, signal) => transcribePcm(pcm, {apiKey, signal})}) {
  const target = new URL(upstream);
  if (target.protocol !== 'http:' || target.hostname !== '127.0.0.1' || target.username || target.password)
    throw new Error('The demo requires a loopback HTTP service.');
  if (!token) throw new Error('LOCAL_ACCESS_TOKEN is required.');
  let transcribing = false;
  return http.createServer({requestTimeout:15000}, async (req, res) => {
    const send = (status, body, type = 'application/json') => {
      res.writeHead(status, {'content-type':type + '; charset=utf-8', 'cache-control':'no-store',
        'x-content-type-options':'nosniff', 'referrer-policy':'no-referrer',
        'content-security-policy':"default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'"});
      res.end(typeof body === 'string' || Buffer.isBuffer(body) ? body : JSON.stringify(body));
    };
    // Prevent other websites using the credential-bearing loopback proxy (including DNS rebinding).
    const origin = `http://127.0.0.1:${req.socket.localPort}`;
    if (req.headers.host !== new URL(origin).host) return send(403, {error:'invalid_host'});
    try {
      if (req.method === 'GET' && files.has(req.url)) {
        const [file, type] = files.get(req.url);
        return send(200, await readFile(new URL(file, import.meta.url)), type);
      }
      const health = req.method === 'GET' && req.url === '/health';
      const audio = req.method === 'POST' && req.url === '/transcribe';
      if (!health && !audio && (req.method !== 'POST' || !routes.has(req.url))) return send(404, {error:'not_found'});
      if (!health && (req.headers.origin !== origin || req.headers['content-type'] !== (audio ? 'application/octet-stream' : 'application/json')))
        return send(403, {error:'same_origin_json_required'});
      let size = 0; const chunks = [];
      for await (const chunk of req) {
        size += chunk.length;
        if (size > (audio ? 1440000 : 65536)) return send(413, {error:'request_too_large'});
        chunks.push(chunk);
      }
      const abort = new AbortController();
      res.on('close', () => { if (!res.writableEnded) abort.abort(); });
      if (audio) {
        if (!size || size % 2) return send(400, {error:'invalid_pcm_audio'});
        if (transcribing) return send(429, {error:'transcription_busy'});
        transcribing = true;
        try { return send(200, {text:await transcribe(Buffer.concat(chunks), abort.signal)}); }
        catch (error) { return send(error.status ?? 502, {error:error.status ? error.message : 'transcription_unavailable'}); }
        finally { transcribing = false; }
      }
      const response = await fetch(new URL(req.url, target), {
        method:req.method, redirect:'error', signal:AbortSignal.any([abort.signal, AbortSignal.timeout(35000)]),
        headers:{'content-type':'application/json', authorization:`Bearer ${token}`},
        ...(health ? {} : {body:Buffer.concat(chunks)}),
      });
      send(response.status, await response.text());
    } catch { if (!res.destroyed) send(502, {error:'demo_service_unavailable'}); }
  });
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  if (process.env.AUTH_MODE !== 'local') throw new Error('The browser demo requires AUTH_MODE=local.');
  const server = createDemoServer({upstream:process.env.DEMO_SERVICE_URL, token:process.env.LOCAL_ACCESS_TOKEN, apiKey:process.env.MODEL_API_KEY});
  server.listen(Number(process.env.DEMO_PORT ?? 8788), '127.0.0.1', () =>
    console.log(`Village lab: http://127.0.0.1:${server.address().port}`));
}
