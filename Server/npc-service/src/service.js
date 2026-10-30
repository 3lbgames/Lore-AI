import http from 'node:http';
import { timingSafeEqual } from 'node:crypto';
import { validateModelReply } from './model-reply.js';
import { validateRelationship, projectRelationship, eligibleDialogueRules } from './relationships.js';

export class ServiceError extends Error {
  constructor(status, code) { super(code); this.status = status; this.code = code; }
}
const reject = (status, code) => { throw new ServiceError(status, code); };
const text = (value, max = 128) => typeof value === 'string' && value.trim().length > 0 && value.length <= max;
function validate(body, route) {
  if (!body || body.version !== 1 || !['sessionId', 'interactionId', 'turnId', 'worldId', 'campaignId', 'npcId', 'writerId'].every(k => text(body[k])) ) reject(400, 'invalid_request');
  if (route === '/v1/memory/load' || route === '/v1/game/load') return;
  if (!Number.isSafeInteger(body.expectedRevision) || body.expectedRevision < 0) reject(400, 'invalid_revision');
  if (route === '/v1/game/commit') {
    const m = body.mutation;
    if (!m || Array.isArray(m) || Object.keys(m).length !== 4 || !text(m.eventId) || !text(m.key) ||
        typeof m.expected !== 'string' || m.expected.length > 2000 || typeof m.value !== 'string' || m.value.length > 2000)
      reject(400, 'invalid_mutation');
    return;
  }
  if (route === '/v1/memory/commit') {
    try { validateRelationship(body.relationship); } catch { reject(400, 'invalid_relationship'); }
    const e = body.event;
    if (!e || !text(e.eventId) || e.turnId !== body.turnId || !['player_input','npc_speech','action_outcome'].includes(e.kind) ||
        !['completed','interrupted','failed'].includes(e.delivery) || typeof e.text !== 'string' || e.text.length > 12000 ||
        (e.delivery === 'completed' && !text(e.text, e.kind === 'player_input' ? 8000 : 12000)) ||
        (e.kind === 'player_input' && e.delivery !== 'completed') ||
        (e.kind === 'action_outcome' && !text(e.text, 12000)) ||
        (e.evidenceKey != null && (typeof e.evidenceKey !== 'string' || e.evidenceKey.length > 128 || (e.evidenceKey && e.kind !== 'action_outcome')))) reject(400, 'invalid_event');
    return;
  }
  if ((body.npcInitiated !== undefined && typeof body.npcInitiated !== 'boolean') ||
      (body.npcInitiated ? body.input !== '' || !text(body.intent, 8000) : !text(body.input, 8000)) ||
      (body.intent != null && (typeof body.intent !== 'string' || body.intent.length > 8000)) ||
      typeof body.situation !== 'string' || body.situation.length > 8000 ||
      !Array.isArray(body.actions) || body.actions.length > 32 ||
      body.actions.some(a => !a || Array.isArray(a) || Object.keys(a).length !== 3 || !text(a.id) || !text(a.targetId) || !text(a.description,2000)) ||
      new Set(body.actions.map(a => a.id)).size !== body.actions.length) reject(400, 'invalid_request');
  const p = body.profile;
  if (!p || !text(p.personality, 8000) || !text(p.voice, 256) ||
      !['facts', 'restrictions'].every(k => Array.isArray(p[k]) && p[k].length <= 100 && p[k].every(v => text(v, 2000)))) reject(400, 'invalid_profile');
  if (p.npcId !== body.npcId) reject(403, 'scope_denied');
  try { validateRelationship(p.relationship); } catch { reject(400, 'invalid_relationship'); }
}
async function readBody(req) {
  let size = 0; const chunks = [];
  for await (const chunk of req) { size += chunk.length; if (size > 65536) reject(413, 'request_too_large'); chunks.push(chunk); }
  try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); } catch { reject(400, 'invalid_json'); }
}
function equal(a, b) { const x = Buffer.from(a ?? ''); const y = Buffer.from(b ?? ''); return x.length === y.length && timingSafeEqual(x, y); }
export function createService({ authMode, localToken, grants, generate, verifyIdentity, memoryStore, timeoutMs = 30000, maxConcurrentRequests = 16 }) {
  if (!['local', 'meta'].includes(authMode)) throw new Error('AUTH_MODE must be local or meta');
  if (authMode === 'local' && !localToken) throw new Error('LOCAL_ACCESS_TOKEN is required');
  if (authMode === 'local' && (!Array.isArray(grants) || !grants.length)) throw new Error('At least one local scope grant is required');
  if (!Number.isSafeInteger(maxConcurrentRequests) || maxConcurrentRequests < 1) throw new Error('maxConcurrentRequests must be a positive integer');
  const active = new Set();
  return http.createServer({ requestTimeout: 15000, headersTimeout: 10000 }, async (req, res) => {
    const abort = new AbortController();
    res.on('close', () => { if (!res.writableEnded) abort.abort(); });
    let timer, owner, body;
    const send = (status, value) => { if (!res.destroyed) { res.writeHead(status, { 'content-type': 'application/json', 'cache-control': 'no-store' }); res.end(JSON.stringify(value)); } };
    try {
      if (req.method === 'GET' && req.url === '/health') return send(200, { status: 'ok', version: 1 });
      if (req.method !== 'POST' || !['/v1/responses','/v1/memory/load','/v1/memory/commit','/v1/game/load','/v1/game/commit'].includes(req.url)) return send(404, { error: 'not_found' });
      if (authMode === 'local') {
        if (!['127.0.0.1', '::1', '::ffff:127.0.0.1'].includes(req.socket.remoteAddress) ||
            !equal(req.headers.authorization, `Bearer ${localToken}`)) reject(401, 'unauthorized');
      }
      body = await readBody(req); validate(body, req.url);
      const work = async () => {
        const playerId = authMode === 'local' ? 'local-player' : await verifyIdentity(body.identity, abort.signal);
        abort.signal.throwIfAborted();
        if (authMode === 'local' && !grants.some(g => g.playerId === playerId && g.worldId === body.worldId && (g.campaignId === body.campaignId || (g.allowNewCampaigns === true && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(body.campaignId))) && g.npcIds.includes(body.npcId))) reject(403, 'scope_denied');
        const gameRequest = req.url.startsWith('/v1/game/');
        const key = JSON.stringify([playerId, body.worldId, body.campaignId, gameRequest ? 'game' : 'npc', ...(gameRequest ? [] : [body.npcId])]);
        if (active.has(key)) reject(429, 'request_in_progress');
        if (active.size >= maxConcurrentRequests) reject(429, 'service_busy');
        owner = key; active.add(owner);
        if (!memoryStore) reject(503, 'storage_unavailable');
        const scope = { playerId, worldId: body.worldId, campaignId: body.campaignId, npcId: body.npcId, writerId: body.writerId };
        if (req.url === '/v1/game/load') return {gameState: await memoryStore.loadGame(scope)};
        if (req.url === '/v1/game/commit') return {gameState: await memoryStore.commitGame(scope, body.expectedRevision, body.mutation, abort.signal)};
        if (req.url === '/v1/memory/commit') return memoryStore.commit(scope, body.expectedRevision, {
          eventId: body.event.eventId, turnId: body.event.turnId, kind: body.event.kind, text: body.event.text, delivery: body.event.delivery,
          ...(body.event.evidenceKey ? { evidenceKey: body.event.evidenceKey } : {})
        }, body.relationship);
        let state = await memoryStore.load(scope);
        abort.signal.throwIfAborted();
        if (req.url === '/v1/memory/load') return state;
        if (state.revision !== body.expectedRevision) reject(409, 'revision_conflict');
        const source = body.npcInitiated ? null : state.history.find(e => e.kind === 'player_input' && e.turnId === body.turnId && e.text === body.input);
        if (!body.npcInitiated && !source) reject(409, 'input_not_committed');
        const history = state.history.filter(e => e.delivery === 'completed' || e.kind === 'action_outcome' || (e.kind === 'npc_speech' && e.text.trim()));
        const relationship = projectRelationship(body.profile.relationship,state.relationship);
        const eligibleRules = !source || relationship?.evaluatedEventIds.includes(source.eventId) ? [] : eligibleDialogueRules(body.profile.relationship,relationship);
        const reply = await generate({ ...body, history, relationship, eligibleRules }, abort.signal);
        abort.signal.throwIfAborted();
        validateModelReply(reply,body.input,eligibleRules,body.actions);
        const after = await memoryStore.load(scope, { includeHistory: false });
        if (after.revision !== state.revision) reject(409, 'revision_conflict');
        if (source && body.profile.relationship) {
          const accepted = await memoryStore.assess(scope,state.revision,source.eventId,body.profile.relationship,reply.relationshipProposal,abort.signal);
          state = { ...state, ...accepted };
        } else state.relationship = relationship;
        abort.signal.throwIfAborted();
        return { text: reply.speech, actionProposal: reply.actionProposal, revision: state.revision, ...(state.relationship ? { relationship: state.relationship } : {}) };
      };
      const timeout = new Promise((_, rejectPromise) => { timer = setTimeout(() => { abort.abort(); rejectPromise(new ServiceError(504, 'request_timeout')); }, timeoutMs); });
      // A timed-out dependency may still be running. Keep its slot until it
      // settles so retries cannot bypass either the NPC or capacity limit.
      const operation = work().finally(() => { if (owner) active.delete(owner); });
      const reply = await Promise.race([operation, timeout]);
      send(200, { version: 1, sessionId: body.sessionId, interactionId: body.interactionId, turnId: body.turnId, status: 'completed', ...reply });
    } catch (error) {
      send(error instanceof ServiceError ? error.status : 502, { version: 1, status: 'failed', error: error instanceof ServiceError ? error.code : 'upstream_unavailable' });
    } finally { clearTimeout(timer); }
  });
}
