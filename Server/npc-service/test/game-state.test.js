import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { createService } from '../src/service.js';
import { PostgresMemoryStore } from '../src/memory.js';

const base={version:1,sessionId:'s',interactionId:'i',turnId:'t',worldId:'world',campaignId:'campaign',npcId:'npc',writerId:'writer'};
const mutation={eventId:'grant',key:'delivery',expected:'',value:'accepted'};
async function fixture(t,memoryStore) {
  const server=createService({authMode:'local',localToken:'test',memoryStore,
    generate:()=>assert.fail('game state must not call model')});
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  t.after(()=>{server.closeAllConnections();server.close();});
  return async(route,patch={},token='test')=>{
    const response=await fetch(`http://127.0.0.1:${server.address().port}/v1/game/${route}`,{method:'POST',headers:{authorization:'Bearer '+token,'content-type':'application/json'},body:JSON.stringify({...base,...patch})});
    return {httpStatus:response.status,...await response.json()};
  };
}
test('game state API authenticates, validates and returns campaign state separately from conversation revision',async t=>{
  const state={revision:4,values:[{key:'delivery',value:'accepted'}]};
  const post=await fixture(t,{
    loadGame:async scope=>{assert.equal(scope.playerId,'local-player');return state;},
    commitGame:async(scope,revision,value)=>{assert.equal(revision,3);assert.deepEqual(value,mutation);return state;}
  });
  const loaded=await post('load');assert.equal(loaded.httpStatus,200);assert.deepEqual(loaded.gameState,state);assert.equal(loaded.revision,undefined);
  assert.deepEqual((await post('commit',{expectedRevision:3,mutation})).gameState,state);
  assert.equal((await post('load',{},'')).httpStatus,401);
  for(const scope of [{campaignId:'ungranted'},{npcId:'ungranted'},{worldId:'ungranted'}]) assert.equal((await post('load',scope)).httpStatus,200);
  for(const invalid of [null,{...mutation,key:''},{...mutation,expected:3},{...mutation,value:[]},{...mutation,extra:true}])
    assert.equal((await post('commit',{expectedRevision:3,mutation:invalid})).httpStatus,400);
});

test('PostgreSQL campaign state survives restart, shares across NPCs, isolates campaigns and applies each transition once', {skip:!process.env.TEST_DATABASE_URL}, async()=>{
  const store=new PostgresMemoryStore(process.env.TEST_DATABASE_URL);
  const scope={playerId:'game-test-'+randomUUID(),worldId:'world',campaignId:randomUUID(),npcId:'npc',writerId:'installation'};
  try {
    await store.migrate();
    assert.deepEqual(await store.loadGame(scope),{revision:0,values:[]});
    const accepted={revision:1,values:[{key:'delivery',value:'accepted'}]};
    assert.deepEqual(await Promise.all([store.commitGame(scope,0,mutation),store.commitGame({...scope,npcId:'other'},0,mutation)]),[accepted,accepted]);
    await assert.rejects(store.commitGame(scope,0,{...mutation,value:'completed'}),{code:'event_conflict'});
    await assert.rejects(store.commitGame(scope,0,{...mutation,eventId:'next'}),{code:'revision_conflict'});
    await assert.rejects(store.commitGame(scope,1,{...mutation,eventId:'next'}),{code:'state_conflict'});
    await assert.rejects(store.loadGame({...scope,writerId:'other'}),{code:'writer_conflict'});
    const complete={eventId:'finish',key:'delivery',expected:'accepted',value:'completed'};
    const completed={revision:2,values:[{key:'delivery',value:'completed'}]};
    assert.deepEqual(await store.commitGame(scope,1,complete),completed);
    assert.deepEqual(await store.commitGame(scope,0,mutation),completed,'retry returns current state without replaying its transition');
    const fresh=new PostgresMemoryStore(process.env.TEST_DATABASE_URL);
    try {assert.deepEqual(await fresh.loadGame({...scope,npcId:'other'}),completed);} finally {await fresh.close();}
    for(const patch of [{campaignId:randomUUID()},{playerId:scope.playerId+'-other'},{worldId:'other'}])
      assert.deepEqual(await store.loadGame({...scope,...patch}),{revision:0,values:[]});
  } finally {
    for(const table of ['campaign_game_events','campaign_game_states','campaign_writers'])
      await store.pool.query(`DELETE FROM ${table} WHERE player_id=ANY($1)`,[[scope.playerId,scope.playerId+'-other']]);
    await store.close();
  }
});

test('pending campaign writes do not block NPC memory requests',async t=>{
  let finish;
  const pending=new Promise(resolve=>{finish=resolve;});
  let started;
  const observed=new Promise(resolve=>{started=resolve;});
  const server=createService({authMode:'local',localToken:'test',memoryStore:{commitGame:async()=>{started();await pending;return {revision:1,values:[]};},load:async()=>({revision:0,history:[]})}});
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  t.after(()=>{finish();server.closeAllConnections();server.close();});
  const post=route=>fetch(`http://127.0.0.1:${server.address().port}/v1/${route}`,{method:'POST',headers:{authorization:'Bearer test','content-type':'application/json'},body:JSON.stringify({...base,expectedRevision:0,mutation})});
  const write=post('game/commit');await observed;
  assert.equal((await post('memory/load')).status,200);
  finish();assert.equal((await write).status,200);
});
