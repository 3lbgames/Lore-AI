import test from 'node:test';
import assert from 'node:assert/strict';
import { DemoClient } from '../demo/client.js';

test('reload during speech resumes an interrupted receipt, never completed playback', async () => {
  const data = new Map(), receipts = [];
  const storage = {getItem:k => data.get(k), setItem:(k,v) => data.set(k,v)};
  const request = async (route, body) => {
    if (route === '/v1/memory/load') return {revision:0, hasSpoken:false, history:[]};
    if (route === '/v1/game/load') return {gameState:{revision:0,values:[{key:'first_delivery',value:'accepted'},{key:'cup_location',value:'inventory'}]}};
    if (route === '/v1/memory/commit') { receipts.push(body.event); return {revision:1}; }
    assert.fail(route);
  };
  let started;
  const playing = new Promise(resolve => { started = resolve; });
  const first = new DemoClient({storage,request,deliver:() => { started(); return new Promise(() => {}); }});
  void first.greet(); await playing;
  const reloaded = new DemoClient({storage,request,deliver:() => assert.fail('must not replay interrupted audio')});
  await reloaded.resume();
  assert.equal(receipts.length, 1);
  assert.equal(receipts[0].kind, 'npc_speech');
  assert.equal(receipts[0].delivery, 'interrupted');
  assert.match(receipts[0].text, /Halt! Who goes there/);
});

test('a concurrent world transition cancels the rejected action without recording success', async () => {
  const data = new Map(); let changed = false, worldWrites = 0;
  const storage = {getItem:k => data.get(k), setItem:(k,v) => data.set(k,v)};
  const request = async route => {
    if (route === '/v1/memory/load') return {revision:0, history:[]};
    if (route === '/v1/game/load') return {gameState:changed ? {revision:1,values:[{key:'first_delivery',value:'completed'}]} : {revision:0,values:[{key:'first_delivery',value:'accepted'},{key:'cup_location',value:'inventory'}]}};
    if (route === '/v1/game/commit') {
      worldWrites++; changed = true;
      throw Object.assign(new Error('revision_conflict'), {code:'revision_conflict'});
    }
    assert.fail('must not manufacture an outcome for a rejected action');
  };
  const client = new DemoClient({storage,request,deliver:async () => 'completed'});
  await client.select('browser-osric');
  await assert.rejects(client.act('give_mug'), /revision_conflict/);
  assert.equal(client.state.pending, null);
  assert.equal(client.values.first_delivery, 'completed');
  await client.resume();
  assert.equal(worldWrites, 1);
});
