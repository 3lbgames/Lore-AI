import http from 'node:http';
import { timingSafeEqual } from 'node:crypto';

export class ServiceError extends Error {
  constructor(status, code) { super(code); this.status = status; this.code = code; }
}
const reject = (status, code) => { throw new ServiceError(status, code); };
const text = (value, max = 128) => typeof value === 'string' && value.trim().length > 0 && value.length <= max;
function validate(body) {
  if (!body || body.version !== 1 || !['sessionId', 'interactionId', 'turnId', 'worldId', 'campaignId', 'npcId'].every(k => text(body[k])) ||
      !text(body.input, 8000) || typeof body.situation !== 'string' || body.situation.length > 8000 ||
      !Array.isArray(body.actions) || body.actions.length !== 0) reject(400, 'invalid_request');
  const p = body.profile;
  if (!p || !text(p.personality, 8000) || !text(p.voice, 256) ||
      !['facts', 'restrictions'].every(k => Array.isArray(p[k]) && p[k].length <= 100 && p[k].every(v => text(v, 2000)))) reject(400, 'invalid_profile');
  if (p.npcId !== body.npcId) reject(403, 'scope_denied');
}
async function readBody(req) {
  let size = 0; const chunks = [];
  for await (const chunk of req) { size += chunk.length; if (size > 65536) reject(413, 'request_too_large'); chunks.push(chunk); }
  try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); } catch { reject(400, 'invalid_json'); }
}
function equal(a, b) { const x = Buffer.from(a ?? ''); const y = Buffer.from(b ?? ''); return x.length === y.length && timingSafeEqual(x, y); }
export function createService({ authMode, localToken, grants, generate, verifyIdentity, timeoutMs = 30000 }) {
  if (!['local', 'meta'].includes(authMode)) throw new Error('AUTH_MODE must be local or meta');
  if (authMode === 'local' && !localToken) throw new Error('LOCAL_ACCESS_TOKEN is required');
  if (!Array.isArray(grants) || !grants.length) throw new Error('At least one scope grant is required');
  const active = new Set();
  return http.createServer({ requestTimeout: 15000, headersTimeout: 10000 }, async (req, res) => {
    const abort = new AbortController();
    res.on('close', () => { if (!res.writableEnded) abort.abort(); });
    let timer, owner, body;
    const send = (status, value) => { if (!res.destroyed) { res.writeHead(status, { 'content-type': 'application/json', 'cache-control': 'no-store' }); res.end(JSON.stringify(value)); } };
    try {
      if (req.method === 'GET' && req.url === '/health') return send(200, { status: 'ok', version: 1 });
      if (req.method !== 'POST' || req.url !== '/v1/responses') return send(404, { error: 'not_found' });
      if (authMode === 'local') {
        if (!['127.0.0.1', '::1', '::ffff:127.0.0.1'].includes(req.socket.remoteAddress) ||
            !equal(req.headers.authorization, `Bearer ${localToken}`)) reject(401, 'unauthorized');
      }
      body = await readBody(req); validate(body);
      const work = async () => {
        const playerId = authMode === 'local' ? 'local-player' : await verifyIdentity(body.identity, abort.signal);
        abort.signal.throwIfAborted();
        if (!grants.some(g => g.playerId === playerId && g.worldId === body.worldId && g.campaignId === body.campaignId && g.npcIds.includes(body.npcId))) reject(403, 'scope_denied');
        if (active.has(playerId)) reject(429, 'request_in_progress');
        owner = playerId; active.add(owner);
        const reply = await generate(body, abort.signal);
        if (!text(reply, 12000)) reject(502, 'invalid_model_response');
        return reply;
      };
      const timeout = new Promise((_, rejectPromise) => { timer = setTimeout(() => { abort.abort(); rejectPromise(new ServiceError(504, 'request_timeout')); }, timeoutMs); });
      const reply = await Promise.race([work(), timeout]);
      send(200, { version: 1, sessionId: body.sessionId, interactionId: body.interactionId, turnId: body.turnId, status: 'completed', text: reply });
    } catch (error) {
      send(error.status ?? 502, { version: 1, status: 'failed', error: error.code ?? 'upstream_unavailable' });
    } finally { clearTimeout(timer); if (owner) active.delete(owner); }
  });
}
