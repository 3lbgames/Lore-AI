import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { PostgresMemoryStore } from '../src/memory.js';
import { createService } from '../src/service.js';

const config = { categories:[{id:'trust',description:'Trust',expression:'Be open',initial:0,min:-100,max:100}], rules: [
  { id: 'support', source: 'dialogue', condition: 'Support my ambition', meaning: 'The player supported my ambition.', effects:[{category:'trust',delta:10}] },
  { id: 'gift', source: 'action', evidenceKey: 'need-met', condition: 'Receive what I need', meaning: 'The player met my need.', effects:[{category:'trust',delta:20}] }
] };
test('relationship commits survive restart, retries and isolated NPC/campaign histories', { skip: !process.env.TEST_DATABASE_URL }, async () => {
  const store = new PostgresMemoryStore(process.env.TEST_DATABASE_URL);
  const scope = { playerId: 'test-' + randomUUID(), worldId: 'world', campaignId: randomUUID(), npcId: 'one', writerId: 'writer' };
  try {
    await store.migrate();
    const input = { eventId: 'input', turnId: 'turn', kind: 'player_input', text: 'You can do it.', delivery: 'completed' };
    await store.commit(scope, 0, input);
    const proposal = { ruleId: 'support', evidence: input.text };
    const first = await store.assess(scope, 1, input.eventId, config, proposal);
    assert.equal(first.relationship.values[0].value, 10);
    await assert.rejects(store.assess(scope,1,input.eventId,config,proposal),{code:'revision_conflict'});
    assert.deepEqual(await store.assess(scope,2,input.eventId,config,proposal),first);
    const gift = { eventId: 'gift', turnId: 'gift', kind: 'action_outcome', text: 'Transferred item', delivery: 'completed', evidenceKey: 'need-met' };
    const receipt = await store.commit(scope, 2, gift, config);
    assert.equal(receipt.relationship.values[0].value, 30);
    assert.deepEqual(await store.commit(scope, 2, gift, config), receipt);
    const restored = new PostgresMemoryStore(process.env.TEST_DATABASE_URL);
    try {
      assert.equal((await restored.load(scope)).relationship.values[0].value, 30);
      for (const patch of [{ npcId: 'two' }, { campaignId: randomUUID() }])
        assert.equal((await restored.load({ ...scope, ...patch })).relationship, undefined);
    } finally { await restored.close(); }
    await assert.rejects(store.assess(scope, 1, 'missing', config, proposal), { code: 'revision_conflict' });
    await assert.rejects(store.load({ ...scope, writerId: 'other' }), { code: 'writer_conflict' });
  } finally {
    for (const table of ['conversation_events', 'conversation_streams', 'campaign_writers'])
      await store.pool.query(`DELETE FROM ${table} WHERE player_id=$1`, [scope.playerId]);
    await store.close();
  }
});

test('HTTP commits only after successful single generation and does not reward an evaluated retry', { skip: !process.env.TEST_DATABASE_URL }, async t => {
  const store = new PostgresMemoryStore(process.env.TEST_DATABASE_URL);
  await store.migrate();
  const campaignId = randomUUID();
  let generations = 0, fail = true;
  const server = createService({ authMode: 'local', localToken: 'test', memoryStore: store,
    generate: async body => { generations++; if (fail) throw new Error('provider unavailable'); return {speech:'Thanks for believing in me.',relationshipProposal:body.eligibleRules.length?{ruleId:'support',evidence:body.input}:{ruleId:null,evidence:null},actionProposal:null}; }
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  t.after(async () => {
    server.closeAllConnections(); server.close();
    for (const table of ['conversation_events', 'conversation_streams', 'campaign_writers'])
      await store.pool.query(`DELETE FROM ${table} WHERE player_id=$1 AND world_id=$2 AND campaign_id=$3`, ['local-player', 'relationship-test', campaignId]);
    await store.close();
  });
  const base = { version: 1, sessionId: 's', interactionId: 'i', turnId: 't', worldId: 'relationship-test', campaignId, npcId: 'one', writerId: 'w' };
  async function post(route, patch) {
    const res = await fetch(`http://127.0.0.1:${server.address().port}/v1/${route}`, { method: 'POST', headers: { authorization: 'Bearer test', 'content-type': 'application/json' }, body: JSON.stringify({ ...base, ...patch }) });
    return { statusCode: res.status, ...await res.json() };
  }
  const input = 'Give your business a try.';
  await post('memory/commit', { expectedRevision: 0, event: { eventId: 't:input', turnId: 't', kind: 'player_input', text: input, delivery: 'completed' } });
  const request = { input, profile: { npcId: 'one', personality: 'Hopeful', voice: 'voice', facts: [], restrictions: [], relationship: config }, situation: '', actions: [] };
  assert.equal((await post('responses', { ...request, expectedRevision: 1 })).statusCode, 502);
  const loaded = await post('memory/load');
  assert.equal(loaded.relationship, undefined);
  assert.equal(loaded.history.length, 1);
  fail = false;
  const reply = await post('responses', { ...request, expectedRevision: loaded.revision });
  assert.equal(reply.statusCode, 200);
  assert.equal(reply.relationship.values[0].value, 10);
  assert.equal(generations, 2);
  const retry = await post('responses',{...request,expectedRevision:reply.revision});
  assert.equal(retry.statusCode,200);
  assert.deepEqual(retry.relationship,reply.relationship);
  assert.equal(generations,3);
});
