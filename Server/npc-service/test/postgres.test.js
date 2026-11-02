import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { createService, ServiceError } from '../src/service.js';
import { PostgresMemoryStore } from '../src/memory.js';

test('Meta first-use storage isolates players without configured grants', {skip:!process.env.TEST_DATABASE_URL}, async t => {
  const store = new PostgresMemoryStore(process.env.TEST_DATABASE_URL);
  await store.migrate();
  const players = ['test-'+randomUUID(), 'test-'+randomUUID()];
  const server = createService({authMode:'meta',memoryStore:store,
    verifyIdentity:async identity => {
      if (!players.includes(identity?.userId) || identity?.nonce !== 'valid') throw new ServiceError(401,'invalid_identity');
      return identity.userId;
    }});
  await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
  t.after(async () => {
    server.closeAllConnections(); server.close();
    for (const table of ['conversation_events','conversation_streams','campaign_writers'])
      await store.pool.query(`DELETE FROM ${table} WHERE player_id=ANY($1)`,[players]);
    await store.close();
  });
  const body = {version:1,sessionId:'s',interactionId:'i',turnId:'t',writerId:'writer',
    worldId:'new-world',campaignId:randomUUID(),npcId:'new-npc'};
  async function post(route, player, extra={}) {
    const response = await fetch(`http://127.0.0.1:${server.address().port}/v1/memory/${route}`, {
      method:'POST',headers:{'content-type':'application/json'},
      body:JSON.stringify({...body,identity:{userId:player,nonce:'valid'},...extra})});
    return {...await response.json(),httpStatus:response.status};
  }
  assert.equal((await post('load',players[0])).revision,0);
  const event={eventId:'event',turnId:'t',kind:'player_input',text:'Private memory',delivery:'completed'};
  assert.equal((await post('commit',players[0],{expectedRevision:0,event})).revision,1);
  assert.deepEqual((await post('load',players[1],{playerId:players[0]})).history,[]);
  assert.equal((await post('load',players[0])).history[0].text,'Private memory');
  assert.equal((await post('load',players[0],{campaignId:randomUUID()})).revision,0);
  assert.equal((await post('load',players[0],{identity:{userId:players[0],nonce:'invalid'}})).httpStatus,401);
});

test('PostgreSQL atomic receipts, revisions, writer lease, restart and scope isolation', {skip:!process.env.TEST_DATABASE_URL}, async () => {
  const store = new PostgresMemoryStore(process.env.TEST_DATABASE_URL);
  const scope={playerId:'test-'+randomUUID(),worldId:'world',campaignId:randomUUID(),npcId:'barbarian',writerId:'installation'};
  const event={eventId:'input',turnId:'turn',kind:'player_input',text:'Remember the blue kite.',delivery:'completed'};
  try {
    await store.migrate();
    assert.deepEqual(await store.load(scope),{revision:0,hasSpoken:false,history:[]});
    const receipts=await Promise.all([store.commit(scope,0,event),store.commit(scope,0,event)]);
    assert.deepEqual(receipts,[{revision:1},{revision:1}]);
    await assert.rejects(store.commit(scope,0,{...event,text:'different'}),{code:'event_conflict'});
    await assert.rejects(store.commit(scope,0,{...event,eventId:'another'}),{code:'revision_conflict'});
    await assert.rejects(store.load({...scope,writerId:'other-installation'}),{code:'writer_conflict'});
    const restored = new PostgresMemoryStore(process.env.TEST_DATABASE_URL);
    try { assert.deepEqual(await restored.load(scope),{revision:1,hasSpoken:true,history:[event]}); } finally {await restored.close();}
    for (const patch of [{playerId:scope.playerId+'-other'}, {campaignId:randomUUID()}, {npcId:'other'}]) assert.equal((await store.load({...scope,...patch})).hasSpoken,false);
  } finally {
    // Isolated test identities only; never touch application history.
    await store.pool.query('DELETE FROM conversation_events WHERE player_id=ANY($1)',[[scope.playerId,scope.playerId+'-other']]);
    await store.pool.query('DELETE FROM conversation_streams WHERE player_id=ANY($1)',[[scope.playerId,scope.playerId+'-other']]);
    await store.pool.query('DELETE FROM campaign_writers WHERE player_id=ANY($1)',[[scope.playerId,scope.playerId+'-other']]);
    await store.close();
  }
});


test('HTTP → PostgreSQL keeps failed-generation input, excludes undelivered speech and restores full history', {skip:!process.env.TEST_DATABASE_URL}, async t => {
  const store=new PostgresMemoryStore(process.env.TEST_DATABASE_URL);
  await store.migrate();
  const campaignId=randomUUID(), resetCampaignId=randomUUID();
  let fail=true, received;
  const server=createService({authMode:'local',localToken:'test',memoryStore:store,
    generate:async body=>{received=body.history;if(fail) throw new ServiceError(502,'model_unavailable');return {speech:'I remember the blue kite.',relationshipProposal:{ruleId:null,evidence:null},actionProposal:null};}});
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  t.after(async()=>{server.closeAllConnections();server.close();
    for (const [worldId,id] of [['test-world',campaignId],['test-world',resetCampaignId],['other-world',campaignId]]) {
    const key=['local-player',worldId,id];
    for(const table of ['conversation_events','conversation_streams','campaign_writers']) await store.pool.query(`DELETE FROM ${table} WHERE player_id=$1 AND world_id=$2 AND campaign_id=$3`,key);
    }
    await store.close();});
  const body={version:1,sessionId:'session',interactionId:'interaction',turnId:'turn',worldId:'test-world',campaignId,npcId:'npc',writerId:'installation'};
  async function post(route,patch={}) { const response=await fetch(`http://127.0.0.1:${server.address().port}/v1/${route}`,{method:'POST',headers:{authorization:'Bearer test','content-type':'application/json'},body:JSON.stringify({...body,...patch})});return {status:response.status,...await response.json()}; }
  assert.equal((await post('memory/load')).hasSpoken,false);
  const event={eventId:'player',turnId:'turn',kind:'player_input',text:'Remember the blue kite.',delivery:'completed'};
  assert.equal((await post('memory/commit',{expectedRevision:0,event})).revision,1);
  const generation={expectedRevision:1,input:event.text,profile:{npcId:'npc',personality:'A warrior',voice:'voice',facts:[],restrictions:[]},situation:'Nearby',actions:[]};
  assert.equal((await post('responses',generation)).error,'model_unavailable');
  assert.equal((await post('memory/load')).hasSpoken,true);
  assert.equal((await post('memory/commit',{expectedRevision:0,event})).revision,1);
  assert.equal((await post('memory/commit',{expectedRevision:1,event:{eventId:'speech',turnId:'turn',kind:'npc_speech',text:'',delivery:'interrupted'}})).revision,2);
  fail=false;
  assert.equal((await post('responses',{...generation,expectedRevision:2})).text,'I remember the blue kite.');
  assert.deepEqual(received,[event]);
  assert.equal((await post('memory/load')).history.length,2); // generation stores no assistant text
  assert.equal((await post('memory/load',{campaignId:resetCampaignId})).hasSpoken,false);
  assert.equal((await post('memory/load',{worldId:'other-world'})).hasSpoken,false);
  assert.equal((await post('memory/load',{npcId:'other-npc'})).hasSpoken,false);
  assert.equal((await post('responses',{...generation,expectedRevision:1})).error,'revision_conflict');
});
