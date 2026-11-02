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
async function fixture(t, generate = async () => ({speech:'Try the Oak Inn beside the east gate.',relationshipProposal:{ruleId:null,evidence:null},actionProposal:null}), options = {}) {
  const server = createService({ authMode: 'local', localToken: 'test-token',
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
  const post = await fixture(t, async value => { assert.equal(value.input, body.input); return {speech:'Beside the east gate.',relationshipProposal:{ruleId:null,evidence:null},actionProposal:null}; });
  const response = await post(body); assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { version: 1, sessionId: body.sessionId, interactionId: body.interactionId,
    turnId: body.turnId, status: 'completed', text: 'Beside the east gate.', actionProposal: null, revision: 1 });
});
test('rejects missing credentials before generation', async t => {
  const post = await fixture(t, () => assert.fail('unauthorized generation'));
  assert.equal((await post(request(), '')).status, 401);
});
test('rejects mismatched request and profile identities', async t => {
  const post = await fixture(t, () => assert.fail('out of scope generation'));
  for (const patch of [{ npcId: 'other' }, { profile: { ...request().profile, npcId: 'other' } }])
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
    return {speech:'We meet again.',relationshipProposal:{ruleId:null,evidence:null},actionProposal:null};
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
  let release;
  const pending = new Promise(resolve => { release = resolve; });
  const post = await fixture(t, async () => { if (first) { first = false; await pending; } return {speech:'Hello',relationshipProposal:{ruleId:null,evidence:null},actionProposal:null}; });
  assert.equal((await post(request())).status, 504);
  assert.equal((await post(request())).status, 429);
  release();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal((await post(request())).status, 200);
});

test('local authentication accepts Unity world, campaign and NPC IDs without grants', async t => {
  const body = request();
  Object.assign(body, {worldId:'new-world',campaignId:'authored-campaign',npcId:'sorrel'});
  body.profile.npcId = body.npcId;
  const post = await fixture(t, undefined);
  assert.equal((await post(body)).status, 200);
});

test('local persistence routes accept Unity scopes and preserve their IDs', async t => {
  const scopes = [];
  const remember = async scope => {scopes.push(scope); return {revision:0,history:[]};};
  const server = createService({authMode:'local',localToken:'test',memoryStore:{
    load:remember,commit:remember,loadGame:remember,commitGame:remember}});
  await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
  t.after(() => {server.closeAllConnections();server.close();});
  const body = {...request(),worldId:'new-world',campaignId:'authored-campaign',npcId:'flynn',expectedRevision:0,
    event:{eventId:'e',turnId:'turn-1',kind:'player_input',text:'Hello',delivery:'completed'},
    mutation:{eventId:'m',key:'quest',expected:'',value:'started'}};
  for (const route of ['memory/load','memory/commit','game/load','game/commit']) {
    const response = await fetch(`http://127.0.0.1:${server.address().port}/v1/${route}`,{
      method:'POST',headers:{'content-type':'application/json',authorization:'Bearer test'},body:JSON.stringify(body)});
    assert.equal(response.status,200,route);
  }
  assert.equal(scopes.length,4);
  for (const scope of scopes) assert.deepEqual(scope,{playerId:'local-player',worldId:body.worldId,
    campaignId:body.campaignId,npcId:body.npcId,writerId:body.writerId});
});

test('Meta authentication permits first use without player or content grants', async t => {
  let generations = 0;
  const post = await fixture(t, async () => { generations++; return {speech:'Hello',relationshipProposal:{ruleId:null,evidence:null},actionProposal:null}; }, {
    authMode: 'meta',
    verifyIdentity: async identity => {
      if (identity?.nonce !== 'valid-proof') throw new ServiceError(401, 'invalid_identity');
      return identity.userId;
    }
  });
  assert.equal((await post({ ...request(), identity: { userId: 'local-player', nonce: 'bad' } })).status, 401);
  assert.equal(generations, 0);
  const body = {...request(), worldId:'new-world',campaignId:'new-campaign',npcId:'new-npc',
    profile:{...request().profile,npcId:'new-npc'},identity:{userId:'new-player',nonce:'valid-proof'}};
  assert.equal((await post(body)).status, 200);
  assert.equal(generations, 1);
});

test('Meta storage scope uses verified identity on every persistence route', async t => {
  const seen = [];
  const remember = async scope => { seen.push(scope); return {revision:0, history:[]}; };
  const server = createService({authMode:'meta', verifyIdentity:async () => 'verified-player',
    memoryStore:{load:remember,commit:remember,loadGame:remember,commitGame:remember}});
  await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
  t.after(() => {server.closeAllConnections();server.close();});
  for (const route of ['memory/load','memory/commit','game/load','game/commit']) {
    const body = {...request(),playerId:'someone-else',identity:{userId:'spoofed'},expectedRevision:0,
      event:{eventId:'event',turnId:'turn-1',kind:'player_input',text:'Hello',delivery:'completed'},
      mutation:{eventId:'game-event',key:'door',expected:'',value:'open'}};
    const response = await fetch(`http://127.0.0.1:${server.address().port}/v1/${route}`,{
      method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify(body)});
    assert.equal(response.status,200,route);
  }
  assert.equal(seen.length,4);
  for (const scope of seen) assert.equal(scope.playerId,'verified-player');
});

test('timed out identity verification cannot generate later or block the next turn', async t => {
  let first = true, generations = 0;
  const post = await fixture(t, async () => { generations++; return {speech:'Hello',relationshipProposal:{ruleId:null,evidence:null},actionProposal:null}; }, {
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

test('single generation sees preturn values, then commits proposal and retry receives no rules',async t=>{
 const config={categories:[{id:'trust',description:'Trust',expression:'Open',initial:0,min:0,max:10}],rules:[{id:'support',source:'dialogue',condition:'Support',meaning:'Supported',effects:[{category:'trust',delta:2}]}]};
 const {assessRelationship}=await import('../src/relationships.js');
 const body=request();body.profile.relationship=config;
 const event={eventId:'input',kind:'player_input',turnId:body.turnId,text:body.input,delivery:'completed'};
 let state={revision:1,history:[event]},calls=0,assessments=0;
 const post=await fixture(t,async value=>{
  calls++;assert.equal(value.relationship.values[0].value,calls===1?0:2);
  assert.equal(value.eligibleRules.length,calls===1?1:0);
  return {speech:'Thanks',relationshipProposal:calls===1?{ruleId:'support',evidence:body.input}:{ruleId:null,evidence:null},actionProposal:null};
 },{memoryStore:{load:async()=>structuredClone(state),assess:async(_,revision,id,c,p)=>{
  assessments++;assert.equal(revision,state.revision);assert.equal(id,event.eventId);
  if(!state.relationship?.evaluatedEventIds.includes(id)) state={...state,revision:state.revision+1,relationship:assessRelationship(c,state.relationship,event,p)};
  return state;
 }}});
 const first=await(await post(body)).json();assert.equal(first.text,'Thanks');assert.equal(first.relationship.values[0].value,2);
 const retry=await(await post({...body,expectedRevision:first.revision})).json();assert.deepEqual(retry.relationship,first.relationship);assert.equal(calls,2);assert.equal(assessments,2);
});
test('service independently rejects action proposals and stale responses before relationship commit',async t=>{
 for(const stale of [false,true]) {
  const body=request();body.profile.relationship={categories:[{id:'trust',description:'Trust',expression:'Open',initial:0,min:0,max:10}],rules:[]};
  let loads=0;
  const post=await fixture(t,async()=>({speech:'Hello',relationshipProposal:{ruleId:null,evidence:null},actionProposal:stale?null:{id:'open'}}),{memoryStore:{load:async()=>({revision:stale&&++loads>1?2:1,history:[{eventId:'input',kind:'player_input',turnId:body.turnId,text:body.input,delivery:'completed'}]}),assess:()=>assert.fail('invalid output committed')}});
  const response=await post(body);assert.equal(response.status,stale?409:502);assert.equal((await response.json()).error,stale?'revision_conflict':'invalid_action_proposal');
 }
});

for (const delivery of ['interrupted', 'failed']) {
 test(`captioned speech with ${delivery} audio remains in model history`, async t => {
  const history=[{kind:'npc_speech',text:'The inn is east.',delivery}];
  const post=await fixture(t,async value=>{assert.deepEqual(value.history,history);return {speech:'As I mentioned, east.',relationshipProposal:{ruleId:null,evidence:null},actionProposal:null};},{memoryStore:{load:async()=>({revision:1,history})}});
  assert.equal((await post({...request(),npcInitiated:true,input:'',intent:'Continue our conversation'})).status,200);
 });
}
