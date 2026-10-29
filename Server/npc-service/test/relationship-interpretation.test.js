import test from 'node:test';
import assert from 'node:assert/strict';
import { metaGenerator } from '../src/meta.js';
const request={input:'You can do it',profile:{personality:'Curious',relationship:{categories:[{id:'trust',description:'Trust',expression:'Be open',initial:0,min:0,max:10}],rules:[]}},eligibleRules:[{id:'support',condition:'Clearly supportive',effects:[{category:'trust',delta:5}]}],relationship:{values:[{category:'trust',value:0}],experiences:[]}};
const reply={speech:'Thank you.',relationshipProposal:{ruleId:'support',evidence:'You can do it'},actionProposal:null};
const completion = value => Response.json({status:'completed',output:[{type:'message',role:'assistant',content:[{type:'output_text',text:typeof value==='string'?value:JSON.stringify(value)}]}]});
test('one call returns speech and proposal with strict schema, minimal reasoning and no stream',async()=>{
 let calls=0;
 const generate=metaGenerator({apiKey:'secret',fetchImpl:async(_,options)=>{
  calls++; const b=JSON.parse(options.body);
  assert.deepEqual(b.reasoning,{effort:'minimal'});assert.equal(b.stream,false);assert.equal(b.text.format.strict,true);
  assert.deepEqual(b.text.format.schema.properties.relationshipProposal.properties.ruleId.enum,['support',null]);
  assert.deepEqual(b.text.format.schema.properties.actionProposal,{type:'null'});
  const context=JSON.parse(b.input[0].content);
  assert.deepEqual(context.rules,[{id:'support',condition:'Clearly supportive'}]);
  assert.equal(context.profile.relationship,undefined);assert.equal(context.relationship.categories[0].max,10);
  assert.equal(/birthday|business/.test(b.instructions),false);
  return completion(reply);
 }});
 assert.deepEqual(await generate(request),reply);assert.equal(calls,1);
});
test('abstention and null-only eligibility; invalid output never escapes',async()=>{
 const abstain={...reply,relationshipProposal:{ruleId:null,evidence:null}};
 const generate=(value,req=request)=>metaGenerator({apiKey:'secret',fetchImpl:async()=>completion(value)})(req);
 assert.deepEqual(await generate(abstain,{...request,eligibleRules:[]}),abstain);
 for(const value of ['not json',{...reply,extra:1},{...reply,speech:null},{...reply,relationshipProposal:{ruleId:'unknown',evidence:request.input}},{...reply,relationshipProposal:{ruleId:'support',evidence:'invented'}},{...reply,relationshipProposal:{ruleId:null,evidence:'quote'}}]) await assert.rejects(generate(value),{code:'invalid_model_response'});
 await assert.rejects(generate(reply,{...request,eligibleRules:[]}),{code:'invalid_model_response'});
 await assert.rejects(generate({...reply,actionProposal:{id:'gift'}}),{code:'invalid_action_proposal'});
});
test('reasoning override is bounded and provider failures preserve signal',async()=>{
 assert.throws(()=>metaGenerator({reasoning:'none'}));
 const signal=new AbortController().signal;
 const generate=metaGenerator({apiKey:'secret',reasoning:'low',fetchImpl:async(_,options)=>{assert.equal(options.signal,signal);assert.equal(JSON.parse(options.body).reasoning.effort,'low');return new Response('',{status:429});}});
 await assert.rejects(generate(request,signal),{code:'model_rate_limited'});
});
