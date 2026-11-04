import test from 'node:test';
import assert from 'node:assert/strict';
import { DemoClient } from '../demo/client.js';
import { characters, selectGreeting } from '../demo/profiles.js';
import { validateRelationship } from '../src/relationships.js';

function fixture() {
  const data = new Map();
  const storage = {getItem:key => data.get(key), setItem:(key,value) => data.set(key,value)};
  const calls = [], events = new Map(), otherEvents = new Map();
  let revision = 0, game = {revision:0,values:[{key:'first_delivery',value:'accepted'},{key:'cup_location',value:'inventory'}]}, loseSpeechAck = false;
  const request = async (route, body) => {
    calls.push({route,body:structuredClone(body)});
    if (body.npcId !== 'browser-osric' && route.startsWith('/v1/memory/')) {
      const history = otherEvents.get(body.npcId) ?? new Map(); otherEvents.set(body.npcId, history);
      if (route.endsWith('/load')) return {revision:history.size, history:[...history.values()]};
      history.set(body.event.eventId, body.event); return {revision:history.size};
    }
    if (route === '/v1/memory/load') return {revision, hasSpoken:events.size > 0, history:[...events.values()]};
    if (route === '/v1/game/load') return {gameState:structuredClone(game)};
    if (route === '/v1/memory/commit') {
      if (!events.has(body.event.eventId)) {
        assert.equal(body.expectedRevision, revision);
        events.set(body.event.eventId, body.event); revision++;
      }
      if (body.event.kind === 'npc_speech' && loseSpeechAck) { loseSpeechAck = false; throw new Error('lost acknowledgement'); }
      return {revision};
    }
    if (route === '/v1/responses') {
      assert.ok([...events.values()].some(e => e.kind === 'player_input' && e.turnId === body.turnId && e.text === body.input));
      assert.equal(body.expectedRevision, revision);
      return {revision, text:'Thank you for caring about the village.', actionProposal:null};
    }
    if (route === '/v1/game/commit') {
      assert.equal(body.expectedRevision, game.revision);
      assert.equal(body.mutation.expected, 'accepted');
      game = {revision:game.revision + 1, values:[{key:body.mutation.key,value:body.mutation.value}]};
      return {gameState:structuredClone(game)};
    }
    assert.fail(route);
  };
  return {storage,request,calls,events,loseAck:() => { loseSpeechAck = true; }};
}

test('chat commits player input before generation and records actual delivery status', async () => {
  const f = fixture(); const client = new DemoClient({...f,deliver:async () => 'interrupted'});
  await client.select('browser-osric');
  await client.turn('I will protect the homes along our delivery routes.');
  assert.deepEqual([...f.events.values()].map(e => [e.kind,e.delivery]), [['player_input','completed'],['npc_speech','interrupted']]);
  assert.equal(client.state.pending, null);
  assert.equal(f.calls.find(c => c.route === '/v1/responses').body.worldId, 'browser-demo');
});

test('lost speech acknowledgement survives reload and retries the identical event without replaying audio', async () => {
  const f = fixture(); f.loseAck(); let plays = 0;
  const client = new DemoClient({...f,deliver:async () => { plays++; return 'completed'; }});
  await client.select('browser-osric');
  await assert.rejects(client.turn('Hello'), /lost acknowledgement/);
  const original = f.calls.at(-1).body;
  const resumed = new DemoClient({...f,deliver:async () => { plays++; return 'completed'; }});
  await resumed.load(); await resumed.resume();
  const retried = f.calls.filter(c => c.body?.event?.kind === 'npc_speech').at(-1).body;
  assert.deepEqual(retried, original);
  assert.equal(plays, 1); assert.equal(f.events.size, 2);
  assert.equal(resumed.state.pending, null);
});

test('world execution persists state before observed evidence; failed execution never changes state', async () => {
  const f = fixture(); const client = new DemoClient({...f,deliver:async () => 'completed'});
  await client.select('browser-osric');
  await client.act('give_mug', 'failed');
  assert.equal(f.calls.filter(c => c.route === '/v1/game/commit').length, 0);
  assert.equal([...f.events.values()][0].delivery, 'failed');
  await client.act('give_mug');
  const writes = f.calls.filter(c => c.route.endsWith('/commit')).slice(1,3);
  assert.deepEqual(writes.map(c => c.route), ['/v1/game/commit','/v1/memory/commit']);
  assert.equal(writes[1].body.event.evidenceKey, 'need-met');
  assert.equal(client.values.first_delivery, 'completed');
  assert.equal(client.actions.length, 0);
  assert.equal([...f.events.values()].at(-1).text, "Thank you. That's just what I needed.");
  assert.ok(f.calls.some(c => c.route === '/v1/memory/commit' && c.body.npcId === 'browser-mira'));
});

test('authored relationships pass the real service validator', () => {
  for (const character of Object.values(characters)) validateRelationship(character.profile.relationship);
});

test('all four profiles use isolated browser IDs and ignore legacy pending operations', () => {
  assert.deepEqual(Object.keys(characters), ['browser-osric','browser-sorrel','browser-mira','browser-flynn']);
  const storage = {getItem:key => key === 'village-demo' ? JSON.stringify({npcId:'Osric',pending:{stage:'game'}}) : null, setItem:() => {}};
  const client = new DemoClient({storage});
  assert.equal(client.state.npcId, 'browser-flynn');
  assert.equal(client.state.pending, null);
  for (const [id, character] of Object.entries(characters)) assert.equal(character.profile.npcId, id);
});

test('cup availability, NPC proposal, collection and delivery follow persisted object states', async () => {
  const {worldActions, actionOptions} = await import('../demo/profiles.js');
  const trust = value => ({values:[{category:'trust',value}]});
  const playerActions = (id, values, relationship) => worldActions(id, values, relationship).filter(a => a.actor === 'player');
  assert.deepEqual(playerActions('browser-osric', {}, trust(100)), []);
  assert.ok(!actionOptions('browser-mira', {}, trust(14)).some(a => a.id === 'grant_first_delivery'));
  assert.ok(actionOptions('browser-mira', {}, trust(15)).some(a => a.id === 'grant_first_delivery'));
  const client = new DemoClient({storage:{getItem:() => null,setItem:() => {}}});
  client.state.npcId = 'browser-mira'; client.state.memory.relationship = trust(15);
  assert.deepEqual(client.actions, []);
  client.state.proposal = {actionId:'grant_first_delivery',targetId:'wrong-target'};
  assert.deepEqual(client.actions, []);
  client.state.proposal.targetId = 'first_delivery';
  assert.equal(client.actions[0].id, 'grant_first_delivery');
  const table = {first_delivery:'accepted'};
  assert.deepEqual(playerActions('browser-mira', table, trust(15)).map(a => a.id), ['collect_cup']);
  assert.deepEqual(playerActions('browser-osric', table, trust(15)), []);
  const carried = {...table,cup_location:'inventory'};
  assert.deepEqual(playerActions('browser-mira', carried, trust(15)), []);
  assert.equal(playerActions('browser-osric', carried, trust(15))[0].evidenceKey, 'need-met');
  const delivered = {...carried,first_delivery:'completed'};
  assert.deepEqual(playerActions('browser-osric', delivered, trust(15)), []);
  assert.deepEqual(playerActions('browser-mira', delivered, trust(15)), []);
  assert.ok(!actionOptions('browser-flynn', {}).some(a => a.id === 'climb_down_ladder'));
  assert.ok(actionOptions('browser-flynn', {house_fire_rescue:'ladder_raised'}).some(a => a.id === 'climb_down_ladder'));
  assert.ok(!worldActions('browser-flynn', {house_fire_rescue:'rescued_ladder'}, trust(0)).some(a => a.id === 'climb_down_ladder'));

});

test('same-turn NPC proposal executes once; lost outcome acknowledgement resumes without repeating world mutation', async () => {
  const data = new Map(), history = new Map();
  let game = {revision:0,values:[]}, writes = 0, loseAck = true, plays = 0;
  const relationship = {values:[{category:'trust',value:15}]};
  const storage = {getItem:k => data.get(k),setItem:(k,v) => data.set(k,v)};
  const request = async (route, body) => {
    if (route === '/v1/memory/load') return {revision:history.size, history:[...history.values()], relationship};
    if (route === '/v1/game/load') return {gameState:game};
    if (route === '/v1/responses') return {revision:history.size, relationship, text:'I will put the cup out for you.', actionProposal:{actionId:'grant_first_delivery',targetId:'first_delivery'}};
    if (route === '/v1/game/commit') {
      writes++; game = {revision:1,values:[{key:body.mutation.key,value:body.mutation.value}]}; return {gameState:game};
    }
    if (route === '/v1/memory/commit') {
      history.set(body.event.eventId, body.event);
      if (body.event.kind === 'action_outcome' && loseAck) { loseAck = false; throw new Error('lost outcome ack'); }
      return {revision:history.size,relationship};
    }
    assert.fail(route);
  };
  const client = new DemoClient({storage,request,deliver:async () => { plays++; return 'completed'; }});
  await client.select('browser-mira');
  await assert.rejects(client.turn('Please put out the cup.'), /lost outcome ack/);
  assert.equal(client.values.first_delivery, 'accepted');
  assert.equal(plays, 0);
  const resumed = new DemoClient({storage,request,deliver:async () => { plays++; return 'completed'; }});
  await resumed.resume();
  assert.equal(writes, 1);
  assert.equal(plays, 1);
  assert.deepEqual([...history.values()].map(e => e.kind), ['player_input','action_outcome','npc_speech']);
  assert.equal([...history.values()][1].text, 'Action grant_first_delivery on first_delivery completed.');
  assert.equal(resumed.state.proposal, null);
  assert.deepEqual(resumed.actions.map(a => a.id), ['collect_cup']);
});

test('burning the house automatically generates Flynn’s reaction after the outcome, with resumable dialogue failure', async () => {
  const data = new Map(), events = new Map();
  let game = {revision:0,values:[{key:'torch',value:'held_lit'}]}, writes = 0, failedGenerations = 0;
  const storage = {getItem:k => data.get(k),setItem:(k,v) => data.set(k,v)};
  const request = async (route, body) => {
    if (route === '/v1/memory/load') return {revision:events.size,history:[...events.values()]};
    if (route === '/v1/game/load') return {gameState:game};
    if (route === '/v1/game/commit') {
      writes++; game = {revision:1,values:[{key:body.mutation.key,value:body.mutation.value}]}; return {gameState:game};
    }
    if (route === '/v1/memory/commit') { events.set(body.event.eventId,{...body.event,npcId:body.npcId}); return {revision:[...events.values()].filter(e => e.npcId === body.npcId).length}; }
    if (route === '/v1/responses') {
      assert.equal(body.npcInitiated, true);
      assert.equal(body.input, '');
      assert.match(body.intent, /fire/);
      assert.equal([...events.values()].at(-1).kind, 'action_outcome');
      assert.equal(game.values[0].value, 'rescued_fire');
      if (failedGenerations++ < 2) throw new Error('model unavailable');
      return {revision:events.size,text:'You burned the whole house?!',actionProposal:null};
    }
    assert.fail(route);
  };
  const client = new DemoClient({storage,request,deliver:async () => 'completed'});
  await client.select('browser-flynn');
  await client.act('burn_house', 'failed');
  assert.equal(writes, 0);
  await assert.rejects(client.act('burn_house'), /model unavailable/);
  const resumed = new DemoClient({storage,request,deliver:async () => 'completed'});
  await resumed.resume();
  assert.equal(writes, 1);
  assert.deepEqual([...events.values()].filter(e => e.npcId === 'browser-flynn').map(e => [e.kind,e.delivery]), [['action_outcome','failed'],['action_outcome','completed'],['npc_speech','completed']]);
  assert.deepEqual([...events.values()].filter(e => e.npcId !== 'browser-flynn').map(e => e.npcId), ['browser-osric','browser-mira']);
  assert.equal(resumed.state.pending, null);
});


test('Flynn is the fresh-campaign default and uses Unity relationship greetings', async () => {
  const f = fixture(); const client = new DemoClient({...f,deliver:async () => 'completed'});
  assert.equal(client.state.npcId, 'browser-flynn');
  const flynn = characters['browser-flynn'];
  assert.equal(selectGreeting(flynn), "Halt! Who goes there? No, don't answer, I can tell by the smell. What do you want?");
  assert.equal(selectGreeting(flynn, {values:[{category:'annoyance',value:10},{category:'fear',value:20}]}), 'Oh, wonderful. You again. I could smell you coming from the treeline, and I was hoping it was a dead elk.');
  await client.select('browser-osric');
  const restored = new DemoClient({...f});
  assert.equal(restored.state.npcId, 'browser-osric');
  await restored.newCampaign();
  assert.equal(restored.state.npcId, 'browser-flynn');
});

test('all characters deliver their Unity-authored first greetings and select higher-priority relationship bands', async () => {
  const cases = [
    ['browser-osric', 'Hello, traveler. What brings you here?', 'State your business.'],
    ['browser-sorrel', 'Ah, hello. Careful near the orchard wall, the wasps are in a terrible temper today.', "Oh, it's you. Mind the ear, it's still the size of a turnip, and I'm in no mood."],
    ['browser-mira', "Hello! I'm Mira. Have you got a moment?", 'Oh. What is it?'],
    ['browser-flynn', "Halt! Who goes there? No, don't answer, I can tell by the smell. What do you want?", 'Oh, wonderful. You again. I could smell you coming from the treeline, and I was hoping it was a dead elk.'],
  ];
  for (const [npcId, neutral, annoyed] of cases) {
    const events = [], spoken = []; let generated = 0;
    const request = async (route, body) => {
      if (route === '/v1/memory/load') return {revision:events.length,history:events,hasSpoken:events.some(e => e.kind === 'npc_speech' && e.delivery === 'completed')};
      if (route === '/v1/game/load') return {gameState:{revision:0,values:[]}};
      if (route === '/v1/memory/commit') { events.push(body.event); return {revision:events.length}; }
      if (route === '/v1/responses') {
        generated++;
        assert.equal(body.npcInitiated, true);
        assert.equal(body.input, '');
        assert.match(body.situation, /Do not assume a new player utterance or repeat your first introduction/);
        return {revision:events.length,text:'A fresh returning greeting.',actionProposal:null};
      }
      assert.fail(route);
    };
    const client = new DemoClient({storage:{getItem:() => null,setItem:() => {}},request,deliver:async text => {spoken.push(text);return 'completed';}});
    await client.select(npcId); await client.greet();
    assert.deepEqual(spoken, [neutral], npcId);
    assert.equal(generated, 0);
    assert.equal(events[0].text, neutral);
    const relationship = {values:[{category:'trust',value:50},{category:'affection',value:30},{category:'annoyance',value:10}]};
    assert.equal(selectGreeting(characters[npcId], relationship), annoyed, npcId);
    await client.greet();
    assert.equal(generated, 1);
    assert.equal(spoken.at(-1), 'A fresh returning greeting.');
  }
});
