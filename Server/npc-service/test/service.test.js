import test from 'node:test';
import assert from 'node:assert/strict';
import { createService, ServiceError } from '../src/service.js';

const request = () => ({
  version: 1, writerId: 'writer', expectedRevision: 1, sessionId: 'session-1', interactionId: 'interaction-1', turnId: 'turn-1',
  worldId: 'local-world', campaignId: 'local-campaign', npcId: 'barbarian',
  input: 'Where can I get lodging for the night?',
  profile: { npcId: 'barbarian', personality: 'A blunt but helpful warrior.', voice: 'existing',
    facts: ['The Oak Inn is beside the east gate.'], restrictions: ['Do not invent world facts.'] },
  situation: 'The player is nearby.', actions: []
});
async function fixture(t, generate = async () => 'Try the Oak Inn beside the east gate.', options = {}) {
  const server = createService({ authMode: 'local', localToken: 'test-token',
    grants: [{ playerId: 'local-player', worldId: 'local-world', campaignId: 'local-campaign', npcIds: ['barbarian'] }],
    timeoutMs: 50, generate, memoryStore: { load: async () => ({ revision: 1, history: [{kind: 'player_input',turnId:'turn-1',text: fixtureInput,delivery:'completed'}] }) }, ...options });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(() => { server.closeAllConnections(); server.close(); });
  let fixtureInput;
  return async (body, token = 'test-token') => { fixtureInput = body.input; return fetch(`http://127.0.0.1:${server.address().port}/v1/responses`, {
    method: 'POST', headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` }, body: JSON.stringify(body)
  }); };
}
test('returns a complete response with correlation IDs and preserves exact input', async t => {
  const body = request(); body.input = '  Where is "the inn"?\n';
  const post = await fixture(t, async value => { assert.equal(value.input, body.input); return 'Beside the east gate.'; });
  const response = await post(body); assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { version: 1, sessionId: body.sessionId, interactionId: body.interactionId,
    turnId: body.turnId, status: 'completed', text: 'Beside the east gate.', revision: 1 });
});
test('rejects missing credentials before generation', async t => {
  const post = await fixture(t, () => assert.fail('unauthorized generation'));
  assert.equal((await post(request(), '')).status, 401);
});
test('rejects other campaign, NPC and profile scope', async t => {
  const post = await fixture(t, () => assert.fail('out of scope generation'));
  for (const patch of [{ campaignId: 'other' }, { npcId: 'other' }, { profile: { ...request().profile, npcId: 'other' } }])
    assert.equal((await post({ ...request(), ...patch })).status, 403);
});
test('rejects actions and incompatible protocol versions', async t => {
  const post = await fixture(t);
  assert.equal((await post({ ...request(), actions: ['give-gold'] })).status, 400);
  assert.equal((await post({ ...request(), version: 2 })).status, 400);
});

test('NPC-initiated dialogue uses scoped memory without inventing player input', async t => {
  const history = [{kind:'action_outcome',text:'The patrol was interrupted.',delivery:'interrupted'}];
  const post = await fixture(t, async value => {
    assert.deepEqual(value.history, history);
    assert.equal(value.intent, 'Comment on our last encounter');
    assert.equal(value.input, '');
    return 'We meet again.';
  }, { memoryStore: { load: async () => ({revision:1,history}) } });
  const body = {...request(), npcInitiated:true, input:'', intent:'Comment on our last encounter'};
  assert.equal((await post(body)).status, 200);
  assert.equal((await post({...body, intent:''})).status, 400);
  assert.equal((await post({...body, input:'fabricated speech'})).status, 400);
  assert.equal((await post({...body, expectedRevision:0})).status, 409);
  assert.equal((await post({...request(), npcInitiated:false})).status, 409);
});
test('times out and remains usable', async t => {
  let first = true;
  const post = await fixture(t, async () => { if (first) { first = false; await new Promise(resolve => setTimeout(resolve, 100)); } return 'Hello'; });
  assert.equal((await post(request())).status, 504);
  assert.equal((await post(request())).status, 200);
});

test('rejects an ungranted NPC even when the request and profile identities match', async t => {
  const post = await fixture(t, () => assert.fail('ungranted NPC reached generation'));
  const body = request(); body.npcId = 'other'; body.profile.npcId = 'other';
  assert.equal((await post(body)).status, 403);
});

test('Meta proof must resolve to the granted player before generation', async t => {
  let generations = 0;
  const post = await fixture(t, async () => { generations++; return 'Hello'; }, {
    authMode: 'meta',
    verifyIdentity: async identity => {
      if (identity?.nonce !== 'valid-proof') throw new ServiceError(401, 'invalid_identity');
      return identity.userId;
    }
  });
  assert.equal((await post({ ...request(), identity: { userId: 'local-player', nonce: 'bad' } })).status, 401);
  assert.equal((await post({ ...request(), identity: { userId: 'ungranted-player', nonce: 'valid-proof' } })).status, 403);
  assert.equal(generations, 0);
  assert.equal((await post({ ...request(), identity: { userId: 'local-player', nonce: 'valid-proof' } })).status, 200);
  assert.equal(generations, 1);
});

test('timed out identity verification cannot generate later or block the next turn', async t => {
  let first = true, generations = 0;
  const post = await fixture(t, async () => { generations++; return 'Hello'; }, {
    authMode: 'meta',
    verifyIdentity: async () => {
      if (first) { first = false; await new Promise(resolve => setTimeout(resolve, 100)); }
      return 'local-player';
    }
  });
  assert.equal((await post(request())).status, 504);
  assert.equal((await post(request())).status, 200);
  await new Promise(resolve => setTimeout(resolve, 100));
  assert.equal(generations, 1);
});
