import test from 'node:test';
import assert from 'node:assert/strict';
import { createService } from '../src/service.js';
import { validateModelReply } from '../src/model-reply.js';
import { metaGenerator } from '../src/meta.js';

const actions = [{id:'wave',description:'Wave to the player when appropriate.',targetId:'self'}, {id:'walk',description:'Walk to the table.',targetId:'table'}];
const reply = (actionProposal = null, speech = '') => ({speech,relationshipProposal:{ruleId:null,evidence:null},actionProposal});

test('silent turns and action-only turns are valid, with exact catalog membership', () => {
  assert.doesNotThrow(() => validateModelReply(reply(), '', [], []));
  assert.doesNotThrow(() => validateModelReply(reply({actionId:'wave',targetId:'self'}), 'Goodbye', [], actions));
  for (const proposal of [{actionId:'wave',targetId:'table'}, {actionId:'unknown',targetId:'self'}, {actionId:'wave',targetId:'self',extra:true}, [{actionId:'wave',targetId:'self'}]])
    assert.throws(() => validateModelReply(reply(proposal), '', [], actions), {code:'invalid_action_proposal'});
  assert.throws(() => validateModelReply(reply({actionId:'wave',targetId:'self'}), '', [], []), {code:'invalid_action_proposal'});
});

test('Meta schema and context supply only this turn’s catalog and permit silent contextual actions', async () => {
  const expected = reply({actionId:'wave',targetId:'self'});
  const generate = metaGenerator({apiKey:'test',fetchImpl:async (_url, options) => {
    const request = JSON.parse(options.body);
    assert.deepEqual(JSON.parse(request.input[0].content).actions, actions);
    const schema = request.text.format.schema.properties.actionProposal;
    assert.deepEqual(schema.anyOf[1].properties.actionId.enum, ['wave','walk']);
    assert.deepEqual(schema.anyOf[1].properties.targetId.enum, ['self','table']);
    assert.match(request.instructions, /concurrent/i);
    assert.match(request.instructions, /context/i);
    return Response.json({status:'completed',output:[{type:'message',role:'assistant',content:[{type:'output_text',text:JSON.stringify(expected)}]}]});
  }});
  assert.deepEqual(await generate({profile:{},input:'Goodbye',actions}), expected);
});

test('HTTP returns structured action-only output and rejects invalid catalogs before generation', async t => {
  let generations = 0;
  const server = createService({authMode:'local',localToken:'test',memoryStore:{load:async()=>({revision:0,history:[]})},generate:async value => {
      generations++; assert.deepEqual(value.actions,actions); return reply({actionId:'wave',targetId:'self'});
    }});
  await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
  t.after(()=>{server.closeAllConnections();server.close();});
  const body={version:1,sessionId:'s',interactionId:'i',turnId:'t',worldId:'world',campaignId:'campaign',npcId:'npc',writerId:'w',expectedRevision:0,
    npcInitiated:true,input:'',intent:'Greet',situation:'Nearby',profile:{npcId:'npc',personality:'Friendly',voice:'voice',facts:[],restrictions:[]},actions};
  const post=patch=>fetch(`http://127.0.0.1:${server.address().port}/v1/responses`,{method:'POST',headers:{authorization:'Bearer test','content-type':'application/json'},body:JSON.stringify({...body,...patch})});
  const response=await post({}); assert.equal(response.status,200);
  const result=await response.json(); assert.equal(result.text,'');assert.deepEqual(result.actionProposal,{actionId:'wave',targetId:'self'});
  for(const invalid of [[...actions,actions[0]], [{id:'bad',description:'Missing target'}], ['wave'], Array.from({length:33},(_,i)=>({...actions[0],id:String(i)}))])
    assert.equal((await post({actions:invalid})).status,400);
  assert.equal(generations,1);
});
