import test from 'node:test';
import assert from 'node:assert/strict';
import { createService } from '../src/service.js';

function deferred() {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
}
function request(npcId = 'one') {
  return {
    version: 1, writerId: 'writer', expectedRevision: 0,
    sessionId: 'session', interactionId: 'interaction', turnId: 'turn',
    worldId: 'world', campaignId: 'campaign', npcId,
    npcInitiated: true, input: '', intent: 'Greet the player', situation: '', actions: [],
    profile: { npcId, personality: 'Helpful', voice: 'voice', facts: [], restrictions: [] }
  };
}
async function fixture(t, generate, options = {}) {
  const commits = [];
  const server = createService({
    authMode: 'local', localToken: 'test', timeoutMs: 1000,
    memoryStore: {
      load: async () => ({ revision: 0, history: [] }),
      commit: async (scope, revision, event) => { commits.push({ scope, event }); return { revision: revision + 1 }; }
    }, generate, ...options
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(() => { server.closeAllConnections(); server.close(); });
  const post = (body, route = '/v1/responses', signal) => fetch(`http://127.0.0.1:${server.address().port}${route}`, {
    method: 'POST', headers: { authorization: 'Bearer test', 'content-type': 'application/json' },
    body: JSON.stringify(body), signal
  });
  return { post, commits };
}

test('another NPC can load, commit, and initiate dialogue during generation for the same player', async t => {
  const started = deferred(), release = deferred();
  const { post, commits } = await fixture(t, async body => {
    if (body.npcId === 'one') { started.resolve(); await release.promise; }
    return {speech:'Hello',relationshipProposal:{ruleId:null,evidence:null},actionProposal:null};
  });
  const first = post(request());
  await started.promise;
  assert.equal((await post(request('two'), '/v1/memory/load')).status, 200);
  const event = { eventId: 'event', turnId: 'turn', kind: 'player_input', delivery: 'completed', text: 'Hello' };
  assert.equal((await post({ ...request('two'), event }, '/v1/memory/commit')).status, 200);
  assert.equal(commits[0].scope.npcId, 'two');
  assert.equal((await post(request('two'))).status, 200);
  release.resolve();
  assert.equal((await first).status, 200);
});

test('overlapping requests to one NPC remain excluded without releasing its active owner', async t => {
  const started = deferred(), release = deferred();
  const { post } = await fixture(t, async () => { started.resolve(); await release.promise; return {speech:'Hello',relationshipProposal:{ruleId:null,evidence:null},actionProposal:null}; });
  const first = post(request());
  await started.promise;
  for (let i = 0; i < 2; i++) {
    const response = await post(request(), '/v1/memory/load');
    assert.equal(response.status, 429);
    assert.equal((await response.json()).error, 'request_in_progress');
  }
  release.resolve();
  assert.equal((await first).status, 200);
  assert.equal((await post(request(), '/v1/memory/load')).status, 200);
});

test('concurrent work has an overall capacity limit and restores capacity on completion', async t => {
  const started = deferred(), release = deferred();
  let count = 0;
  const { post } = await fixture(t, async () => {
    if (++count === 2) started.resolve();
    await release.promise;
    return {speech:'Hello',relationshipProposal:{ruleId:null,evidence:null},actionProposal:null};
  }, { maxConcurrentRequests: 2 });
  const first = post(request()), second = post(request('two'));
  await started.promise;
  const rejected = await post(request('three'), '/v1/memory/load');
  assert.equal(rejected.status, 429);
  assert.equal((await rejected.json()).error, 'service_busy');
  release.resolve();
  assert.equal((await first).status, 200);
  assert.equal((await second).status, 200);
  assert.equal((await post(request('three'), '/v1/memory/load')).status, 200);
});

test('timeout retains the NPC slot until underlying work settles', async t => {
  const release = deferred(), finished = deferred();
  const { post } = await fixture(t, async () => {
    await release.promise;
    finished.resolve();
    return {speech:'Late response',relationshipProposal:{ruleId:null,evidence:null},actionProposal:null};
  }, { timeoutMs: 30 });
  assert.equal((await post(request())).status, 504);
  assert.equal((await post(request(), '/v1/memory/load')).status, 429);
  assert.equal((await post(request('two'), '/v1/memory/load')).status, 200);
  release.resolve();
  await finished.promise;
  await new Promise(resolve => setImmediate(resolve));
  assert.equal((await post(request(), '/v1/memory/load')).status, 200);
});

test('cancelling one NPC does not abort another NPC generation', async t => {
  const started = deferred(), release = deferred(), cancelled = deferred();
  let count = 0;
  const signals = {};
  const { post } = await fixture(t, async (body, signal) => {
    signals[body.npcId] = signal;
    signal.addEventListener('abort', () => cancelled.resolve(), { once: true });
    if (++count === 2) started.resolve();
    await release.promise;
    return {speech:'Hello',relationshipProposal:{ruleId:null,evidence:null},actionProposal:null};
  });
  const controller = new AbortController();
  const first = post(request(), '/v1/responses', controller.signal).catch(error => error);
  const second = post(request('two'));
  await started.promise;
  controller.abort();
  await cancelled.promise;
  assert.equal(signals.one.aborted, true);
  assert.equal(signals.two.aborted, false);
  release.resolve();
  assert.equal((await first).name, 'AbortError');
  assert.equal((await second).status, 200);
});
