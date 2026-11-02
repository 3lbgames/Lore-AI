import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { createService } from '../src/service.js';
import { PostgresMemoryStore } from '../src/memory.js';

test('action outcomes persist and inform NPC speech without player dialogue', {skip:!process.env.TEST_DATABASE_URL}, async t => {
  const store = new PostgresMemoryStore(process.env.TEST_DATABASE_URL);
  await store.migrate();
  const worldId = 'action-test-' + randomUUID(), campaignId = randomUUID();
  let received;
  const server = createService({authMode:'local',localToken:'test',memoryStore:store,
    generate:async body=>{received=body;return {speech:'I remember.',relationshipProposal:{ruleId:null,evidence:null},actionProposal:null};}});
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  t.after(async()=>{
    server.closeAllConnections(); server.close();
    for (const table of ['conversation_events','conversation_streams','campaign_writers'])
      await store.pool.query(`DELETE FROM ${table} WHERE world_id=$1`,[worldId]);
    await store.close();
  });
  const base={version:1,sessionId:'s',interactionId:'i',turnId:'t',worldId,campaignId,npcId:'npc',writerId:'writer'};
  const post=async(route,patch={})=>{
    const response=await fetch(`http://127.0.0.1:${server.address().port}/v1/${route}`,{method:'POST',headers:{authorization:'Bearer test','content-type':'application/json'},body:JSON.stringify({...base,...patch})});
    return {httpStatus:response.status,...await response.json()};
  };
  const events=[];
  for (const delivery of ['completed','failed','interrupted']) {
    const event={eventId:delivery,turnId:'t',kind:'action_outcome',text:'Patrol outcome: '+delivery,delivery};
    assert.equal((await post('memory/commit',{expectedRevision:events.length,event})).revision,events.length+1);
    assert.equal((await post('memory/commit',{expectedRevision:events.length,event})).revision,events.length+1);
    events.push(event);
  }
  assert.equal((await post('memory/commit',{expectedRevision:3,event:{...events[0],text:'changed'}})).error,'event_conflict');
  assert.equal((await post('memory/commit',{expectedRevision:3,event:{...events[0],eventId:'invalid',text:''}})).error,'invalid_event');
  const fresh = new PostgresMemoryStore(process.env.TEST_DATABASE_URL);
  try {
    assert.deepEqual(await fresh.load({playerId:'local-player',worldId,campaignId,npcId:'npc',writerId:'writer'}),{revision:3,hasSpoken:false,history:events});
  } finally {await fresh.close();}
  const generation={expectedRevision:3,npcInitiated:true,input:'',intent:'Recall our last encounter',situation:'Player nearby',actions:[],profile:{npcId:'npc',personality:'A warrior',voice:'voice',facts:[],restrictions:[]}};
  assert.equal((await post('responses',generation)).text,'I remember.');
  assert.deepEqual(received.history,events);
  assert.equal((await post('memory/load')).revision,3,'generation must not invent a persisted utterance');
});
