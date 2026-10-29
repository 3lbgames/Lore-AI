import test from 'node:test';
import assert from 'node:assert/strict';
import { createService } from '../src/service.js';

test('memory load is authenticated and returns persisted recognition state', async t => {
  const server = createService({authMode:'local',localToken:'test',grants:[{playerId:'local-player',worldId:'world',campaignId:'campaign',npcIds:['npc']}],memoryStore:{load:async()=>({revision:2,hasSpoken:true,history:[]})}});
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  t.after(()=>{server.closeAllConnections();server.close();});
  const response=await fetch(`http://127.0.0.1:${server.address().port}/v1/memory/load`,{method:'POST',headers:{authorization:'Bearer test','content-type':'application/json'},body:JSON.stringify({version:1,sessionId:'s',interactionId:'i',turnId:'t',worldId:'world',campaignId:'campaign',npcId:'npc',writerId:'writer'})});
  assert.equal(response.status,200);
  assert.equal((await response.json()).hasSpoken,true);
});
