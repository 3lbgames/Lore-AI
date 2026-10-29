import test from 'node:test';
import assert from 'node:assert/strict';
import { assessRelationship, validateRelationship } from '../src/relationships.js';
const config = { categories: [{id:'trust',description:'Trust',expression:'Be open',initial:0,min:-5,max:15},{id:'respect',description:'Respect',expression:'Be attentive',initial:2,min:0,max:10}], rules:[{id:'support',source:'dialogue',condition:'Clear support',meaning:'Supported me',effects:[{category:'trust',delta:10},{category:'respect',delta:5}],repeat:{maxApplications:2,cooldownSeconds:10}}] };
const event = {eventId:'one',kind:'player_input',delivery:'completed',text:'You can do it'};
const proposal = {ruleId:'support',evidence:event.text};
test('multiple effects clamp atomically and cap/cooldown are independent of event dedup',()=>{
 const first=assessRelationship(config,null,event,proposal,100000);
 assert.deepEqual(first.values,[{category:'trust',value:10},{category:'respect',value:7}]);
 const early=assessRelationship(config,first,{...event,eventId:'early'},proposal,101000);
 assert.equal(early.ruleUsage[0].applications,1);
 assert.ok(early.evaluatedEventIds.includes('early'));
 assert.deepEqual(assessRelationship(config,early,{...event,eventId:'early'},proposal,120000),early);
 const second=assessRelationship(config,early,{...event,eventId:'two'},proposal,110000);
 assert.deepEqual(second.experiences[1].effects,[{category:'trust',delta:5},{category:'respect',delta:3}]);
 assert.equal(assessRelationship(config,second,{...event,eventId:'three'},proposal,130000).ruleUsage[0].applications,2);
 assert.equal(assessRelationship(config,null,event,proposal,100000).ruleUsage[0].applications,1);
});
test('unlimited repetition still deduplicates; omission defaults to once',()=>{
 for(const repeat of [undefined,{maxApplications:0,cooldownSeconds:0}]) {
 const c={...config,rules:[{...config.rules[0],repeat}]};
 const first=assessRelationship(c,null,event,proposal,100000);
 assert.deepEqual(assessRelationship(c,first,event,proposal,110000),first);
 assert.equal(assessRelationship(c,first,{...event,eventId:'two'},proposal,110000).ruleUsage[0].applications,repeat?2:1);
 }
});
test('abstention, false evidence and action claims earn nothing but are evaluated',()=>{
 for(const p of [null,{ruleId:'bad',evidence:event.text},{ruleId:'support',evidence:'invented'}]) {
 const state=assessRelationship(config,null,event,p);
 assert.equal(state.values[0].value,0); assert.deepEqual(state.evaluatedEventIds,['one']);
 }
 const c={...config,rules:[{...config.rules[0],source:'action',evidenceKey:'transfer'}]};
 assert.equal(assessRelationship(c,null,event,proposal).values[0].value,0);
 for(const delivery of ['completed','failed','interrupted']) assert.equal(assessRelationship(c,null,{...event,kind:'action_outcome',evidenceKey:'transfer',delivery}).values[0].value,delivery==='completed'?10:0);
});
test('invalid references, duplicate categories and invalid repeat limits are rejected',()=>{
 for(const c of [{...config,categories:[...config.categories,config.categories[0]]},{...config,rules:[{...config.rules[0],effects:[{category:'missing',delta:1}]}]},{...config,rules:[{...config.rules[0],repeat:{maxApplications:-1,cooldownSeconds:0}}]}]) assert.throws(()=>validateRelationship(c));
});
