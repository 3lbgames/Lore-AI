import test from 'node:test';
import assert from 'node:assert/strict';
import {DemoClient} from '../demo/client.js';
import {actionOptions} from '../demo/profiles.js';

function fixture(npcId, respond, initial = {}) {
  const stored = new Map(), events = [], requests = [], spoken = [];
  let relationship = {values:[{category:'trust',value:initial.trust ?? 0}]};
  let game = {revision:0,values:Object.entries(initial.world ?? {}).map(([key,value]) => ({key,value}))};
  const request = async (route, body) => {
    requests.push({route,body:structuredClone(body)});
    if (route === '/v1/memory/load') return {revision:events.length,history:events,relationship,hasSpoken:initial.hasSpoken ?? false};
    if (route === '/v1/game/load') return {gameState:game};
    if (route === '/v1/memory/commit') { events.push(body.event); return {revision:events.length,relationship}; }
    if (route === '/v1/game/commit') { game = {revision:game.revision+1,values:[...game.values.filter(v => v.key !== body.mutation.key),{key:body.mutation.key,value:body.mutation.value}]}; return {gameState:game}; }
    if (route === '/v1/responses') {
      const response = await respond(body, requests.filter(r => r.route === route).length);
      if (response.relationship) relationship = response.relationship;
      return {revision:events.length,relationship,...response};
    }
    assert.fail(route);
  };
  const client = new DemoClient({storage:{getItem:k => stored.get(k),setItem:(k,v) => stored.set(k,v)}, request,
    deliver:async text => { spoken.push({text,events:structuredClone(events)}); return 'completed'; }});
  client.state.npcId = npcId;
  return {client,events,requests,spoken};
}

test('Unity catalogs expose gestures and only currently permitted quest actions', () => {
  assert.deepEqual(actionOptions('browser-sorrel', {}, {values:[]}).map(a => a.id), ['wave','cheer']);
  assert.equal(actionOptions('browser-sorrel', {}, {values:[{category:'trust',value:15}]}).find(a => a.id === 'give_smokebombs').targetId, 'wasp_nest');
  assert.ok(actionOptions('browser-flynn', {house_fire_rescue:'ladder_raised'}).some(a => a.id === 'climb_down_ladder'));
  assert.ok(actionOptions('browser-mira', {}).some(a => a.id === 'walk_pickup_table'));
});

test('accepted trust unlocks one same-turn reconsideration, with action before delivered final speech', async () => {
  const f = fixture('browser-mira', (body, round) => round === 1
    ? {text:'First draft.',relationship:{values:[{category:'trust',value:15}]},actionProposal:null}
    : {text:'I will put it out.',actionProposal:{actionId:'grant_first_delivery',targetId:'first_delivery'}});
  await f.client.turn('I will help you make a safe first delivery.');
  const requests = f.requests.filter(r => r.route === '/v1/responses').map(r => r.body);
  assert.equal(requests.length, 2);
  assert.equal(requests[0].turnId, requests[1].turnId);
  assert.equal(requests[0].input, requests[1].input);
  assert.ok(!requests[0].actions.some(a => a.id === 'grant_first_delivery'));
  assert.ok(requests[1].actions.some(a => a.id === 'grant_first_delivery'));
  assert.deepEqual(f.spoken.map(s => s.text), ['I will put it out.']);
  assert.ok(f.spoken[0].events.some(e => e.kind === 'action_outcome'));
  assert.equal(f.client.values.first_delivery, 'accepted');
});

test('silent gestures record the action without empty speech receipts', async () => {
  const f = fixture('browser-flynn', () => ({text:'',actionProposal:{actionId:'wave',targetId:'self'}}));
  await f.client.turn('Wave without saying anything.');
  assert.deepEqual(f.events.map(e => e.kind), ['player_input','action_outcome']);
  assert.equal(f.events.at(-1).text, 'Action wave on self completed.');
  assert.equal(f.spoken.length, 0);
});

test('unoffered proposals produce failed receipts without changing the world', async () => {
  const f = fixture('browser-mira', () => ({text:'I can’t do that yet.',actionProposal:{actionId:'grant_first_delivery',targetId:'first_delivery'}}));
  await f.client.turn('Give me the cup.');
  assert.equal(f.client.values.first_delivery, undefined);
  assert.equal(f.events.find(e => e.kind === 'action_outcome')?.delivery, 'failed');
});

test('returning greetings never execute actions and use authored fallback on generation failure', async () => {
  const f = fixture('browser-mira', () => ({text:'Welcome back.',actionProposal:{actionId:'grant_first_delivery',targetId:'first_delivery'}}), {trust:15,hasSpoken:true});
  await f.client.greet();
  assert.equal(f.client.values.first_delivery, undefined);
  const failed = fixture('browser-flynn', () => { throw new Error('model unavailable'); }, {hasSpoken:true});
  await failed.client.greet();
  assert.match(failed.spoken[0].text, /Halt! Who goes there/);
  assert.equal(failed.client.state.pending, null);
});

test('reconsideration failure delivers its first draft, ordinary failure uses Unity fallback', async () => {
  const f = fixture('browser-mira', (body, round) => {
    if (round === 2) throw new Error('model unavailable');
    return {text:'Undelivered first draft.',relationship:{values:[{category:'trust',value:15}]},actionProposal:null};
  });
  await f.client.turn('I can deliver your cup safely.');
  assert.deepEqual(f.spoken.map(s => s.text), ['Undelivered first draft.']);
  assert.equal(f.client.values.first_delivery, undefined);
  const failed = fixture('browser-osric', () => { throw new Error('model unavailable'); });
  await failed.client.turn('Hello.');
  assert.equal(failed.spoken[0].text, 'I cannot talk right now. Try me again in a moment.');
  assert.equal(failed.client.state.pending, null);
});

test('walking changes the simulated position and records the authored action receipt', async () => {
  const f = fixture('browser-osric', () => ({text:'',actionProposal:{actionId:'walk_quiet_spot',targetId:'barbarian_quiet_spot'}}));
  await f.client.turn('Please give me some space.');
  assert.equal(f.client.values['browser-osric_location'], 'barbarian_quiet_spot');
  assert.equal(f.events.at(-1).text, 'Action walk_quiet_spot on barbarian_quiet_spot completed.');
  assert.equal(f.spoken.length, 0);
});

test('physical pickups and ignition gate world actions without inventing NPC memories', async () => {
  const f = fixture('browser-flynn', () => assert.fail('picking up tools must not request a reply'));
  await f.client.load();
  assert.ok(!f.client.actions.some(a => a.id === 'burn_house'));
  await f.client.act('collect_torch');
  assert.ok(!f.client.actions.some(a => a.id === 'burn_house'));
  await f.client.act('light_torch');
  assert.ok(f.client.actions.some(a => a.id === 'burn_house'));
  assert.deepEqual(f.events, []);
  const smoke = fixture('browser-sorrel', () => assert.fail('picking up a smokebomb must not request a reply'), {world:{wasp_nest:'accepted'}});
  await smoke.client.load();
  assert.ok(!smoke.client.actions.some(a => a.id === 'smoke_nest'));
  await smoke.client.act('collect_smokebomb');
  assert.ok(smoke.client.actions.some(a => a.id === 'smoke_nest'));
  assert.deepEqual(smoke.events, []);
});
