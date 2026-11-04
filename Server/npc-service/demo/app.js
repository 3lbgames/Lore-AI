import { DemoClient } from './client.js';
import { characters, npcActions } from './profiles.js';
import { MicrophoneRecorder } from './dictation.js';

const $ = id => document.getElementById(id);
const node = (tag, text, className) => {
  const element = document.createElement(tag); element.textContent = text;
  if (className) element.className = className;
  return element;
};
let busy = false, ready = false, stopVoice, recorder, dictating = false;
const canRecord = !!navigator.mediaDevices?.getUserMedia && !!window.AudioWorkletNode;
const traces = [];
async function request(route, body) {
  const start = performance.now();
  let response, result;
  try {
    response = await fetch(route, {method:'POST', headers:{'content-type':'application/json'}, body:JSON.stringify(body), signal:AbortSignal.timeout(40000)});
    result = await response.json();
    if (!response.ok) {
      const error = new Error(result.error || `HTTP ${response.status}`);
      error.code = result.error; throw error;
    }
    for (const key of ['sessionId','interactionId','turnId']) if (result[key] !== body[key]) throw new Error('Obsolete response correlation ID.');
    return result;
  } finally {
    traces.unshift({route, milliseconds:Math.round(performance.now() - start), status:response?.status ?? 'network error', request:body, response:result});
    traces.length = Math.min(traces.length, 40);
    $('request-count').textContent = `(${traces.length})`;
    $('requests').replaceChildren(...traces.map(trace => {
      const details = node('details', '');
      details.append(node('summary', `${trace.status} · ${trace.route} · ${trace.milliseconds} ms`), node('pre', JSON.stringify(trace, null, 2)));
      return details;
    }));
  }
}
async function deliver(text) {
  $('live').hidden = false; $('live').textContent = text || '(No spoken reply.)';
  if (!text) return 'failed';
  if (!$('voice').checked) {
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    return 'completed';
  }
  return new Promise(resolve => {
    const utterance = new SpeechSynthesisUtterance(text);
    let finished = false;
    const finish = result => {
      if (finished) return;
      finished = true; clearTimeout(timer); stopVoice = null; $('stop').disabled = true;
      resolve(result);
    };
    const timer = setTimeout(() => { finish('failed'); speechSynthesis.cancel(); }, 90000);
    utterance.onend = () => finish('completed');
    utterance.onerror = () => finish('failed');
    stopVoice = () => { finish('interrupted'); speechSynthesis.cancel(); };
    $('stop').disabled = false;
    speechSynthesis.speak(utterance);
  });
}

function render(state) {
  const character = characters[state.npcId];
  $('npc').value = state.npcId; $('avatar').textContent = character.initial;
  $('suggestion').textContent = character.suggestion;
  $('campaign').textContent = state.campaignId;
  $('revisions').textContent = `Memory revision ${state.memory.revision} / world revision ${state.game.revision}`;
  $('profile').textContent = JSON.stringify(character.profile, null, 2);
  const events = state.memory.history ?? [];
  $('history').replaceChildren(...events.map(event => {
    const item = node('div', '', 'bubble ' + event.kind);
    const who = event.kind === 'player_input' ? 'You' : event.kind === 'npc_speech' ? character.name : 'Observed outcome';
    item.append(node('small', `${who} · ${event.delivery}`), document.createTextNode(event.text));
    return item;
  }));
  if (!events.length) $('history').append(node('p', 'Every friendship starts with a hello.', 'empty'));
  $('history').scrollTop = $('history').scrollHeight;
  const relationship = state.memory.relationship;
  $('relationship').replaceChildren(...character.profile.relationship.categories.map(category => {
    const value = relationship?.values.find(v => v.category === category.id)?.value ?? category.initial;
    const wrap = node('div', ''); const label = node('div', '', 'metric');
    label.append(node('span', category.id), node('strong', String(value)));
    const meter = document.createElement('meter'); meter.className = 'meter'; meter.min = category.min; meter.max = category.max; meter.value = value; meter.setAttribute('aria-label', category.id);
    wrap.append(label, meter); return wrap;
  }));
  $('experience').textContent = relationship?.experiences?.at(-1)?.meaning || 'No accepted relationship evidence yet.';
  const values = client.values;
  const labels = {accepted:'Smokebombs on table', cleared_smoke:'Wasps moved safely', cleared_fire:'Nest burned'};
  $('world').replaceChildren(...[
    ['Mira’s cup', values.first_delivery === 'completed' ? 'Delivered to Osric' : values.cup_location === 'inventory' ? 'In your inventory' : values.first_delivery === 'accepted' ? 'On Mira’s table' : 'Unavailable — ask Mira'],
    ['Flynn', {ladder_raised:'Ladder ready', rescued_ladder:'On the ground', rescued_fire:'House burned; on the ground'}[values.house_fire_rescue] || 'On the roof by choice'],
    ['Sorrel’s garden', labels[values.wasp_nest] || 'Blocked by wasps'],
    ['Torch', {held_unlit:'Held, unlit', held_lit:'Held, lit'}[values.torch] || 'At pickup point'],
    ['Smokebombs', values.wasp_nest?.startsWith('cleared') ? 'Quest finished' : values.smokebomb === 'held' ? 'One held' : values.wasp_nest === 'accepted' ? 'On Sorrel’s table' : 'Unavailable'],
    ...(values[state.npcId + '_location'] ? [[character.name + '’s position', values[state.npcId + '_location'].replaceAll('_', ' ')]] : []),
  ].map(([key, value]) => { const row = node('div', '', 'world-row'); row.append(node('span', key), node('span', value)); return row; }));
  $('proposal').textContent = state.proposal ? `Proposed: ${state.proposal.actionId}. The simulation will execute it automatically if its conditions are met.` : 'No pending model action proposal. Available NPC actions: ' + (npcActions(state.npcId, values, relationship).map(a => a.label).join(', ') || 'none') + '. Ask the character to act.';
  const actions = client.actions.filter(action => action.actor !== 'npc');
  $('actions').replaceChildren(...actions.map(action => {
    const button = node('button', 'You · ' + action.label); button.disabled = busy || dictating || !ready || !!state.pending;
    button.addEventListener('click', () => run(() => client.act(action.id, $('fail').checked ? 'failed' : 'completed')));
    return button;
  }));
  $('retry').hidden = !state.pending;
  for (const id of ['npc','greet','send','new','react','message']) $(id).disabled = busy || dictating || !ready || !!state.pending;
  $('dictate').disabled = busy || (!dictating && (!ready || !!state.pending || !canRecord));
  $('dictate').textContent = dictating ? 'Finish dictation' : 'Dictate';
  $('reload').disabled = busy || dictating; $('retry').disabled = busy || dictating || !ready;
}

$('npc').replaceChildren(...Object.entries(characters).map(([id, character]) => {
  const option = node('option', `${character.name} — ${character.role}`); option.value = id; return option;
}));
const client = new DemoClient({storage:localStorage, request, deliver});
client.changed = render;
render(client.state);
async function run(work) {
  if (busy) return;
  busy = true; $('status').className = ''; $('status').textContent = 'Working…'; render(client.state);
  try {
    await work(); ready = true;
    $('status').textContent = 'Saved. The village will remember.';
    $('live').hidden = true;
  } catch (error) {
    $('status').className = 'error';
    const help = {
      demo_service_unavailable:'Start the NPC service on port 8787, then reload state.',
      unauthorized:'Check AUTH_MODE=local and that the demo and service use the same LOCAL_ACCESS_TOKEN.',
      writer_conflict:'Another browser installation holds this campaign’s writer lease. Wait two minutes, then resume.',
      revision_conflict:'State changed in another tab. Reload state, then resume.',
      state_conflict:'The world changed; reload state before choosing a fresh action.',
    };
    $('status').textContent = `${error.message}. ${help[error.code] || 'Your accepted events remain saved. Resume the pending operation to retry.'}`;
  } finally { busy = false; render(client.state); }
}
$('chat').addEventListener('submit', event => {
  event.preventDefault(); const input = $('message').value;
  if (input.trim()) run(async () => { await client.turn(input); $('message').value = ''; });
});
$('npc').addEventListener('change', () => { const id = $('npc').value; run(async () => { await client.select(id); await client.greet(); }); });
$('greet').addEventListener('click', () => run(() => client.greet()));
$('reload').addEventListener('click', () => run(() => client.load()));
$('retry').addEventListener('click', () => run(() => client.resume()));
$('new').addEventListener('click', () => run(async () => { await client.newCampaign(); await client.greet(); }));
$('react').addEventListener('click', () => run(() => client.turn('', 'React briefly to the latest confirmed world outcome you know about. Do not invent an event or a player utterance.')));
$('stop').addEventListener('click', () => stopVoice?.());
async function finishDictation() {
  if (!dictating || busy) return;
  dictating = false; busy = true; render(client.state);
  $('status').textContent = 'Transcribing with Meta…';
  try {
    const audio = await recorder.stop();
    const response = await fetch('/transcribe', {method:'POST', headers:{'content-type':'application/octet-stream'},
      body:audio, signal:AbortSignal.timeout(50000)});
    const result = await response.json();
    if (!response.ok) throw new Error(result.error);
    $('message').value = [$('message').value.trim(), result.text].filter(Boolean).join(' ');
    $('status').textContent = 'Transcript ready. Review it, then send your message.';
  } catch (error) {
    $('status').className = 'error';
    $('status').textContent = error.message === 'no_speech_detected' ? 'No speech detected. Try dictating again.' : `Dictation failed: ${error.message}. Your message has been kept.`;
  } finally { recorder?.cancel(); recorder = null; busy = false; render(client.state); }
}
$('dictate').addEventListener('click', async () => {
  if (dictating) return finishDictation();
  if (busy || !ready) return;
  busy = true; render(client.state); $('status').className = '';
  $('status').textContent = 'Opening microphone…';
  recorder = new MicrophoneRecorder();
  try {
    await recorder.start(finishDictation); dictating = true;
    $('status').textContent = 'Listening… Speak, then choose Finish dictation. Maximum 30 seconds.';
  } catch (error) {
    recorder = null; $('status').className = 'error';
    $('status').textContent = error.name === 'NotAllowedError' ? 'Microphone permission was denied. Allow access to dictate, or type your message.' : `Microphone unavailable: ${error.message}`;
  } finally { busy = false; render(client.state); }
});
window.addEventListener('pagehide', () => recorder?.cancel());
if (!canRecord) $('dictate').title = 'Microphone capture is unavailable in this browser. Type your message instead.';
if (!window.speechSynthesis) { $('voice').disabled = true; $('voice').title = 'Speech synthesis is unavailable in this browser.'; }
window.addEventListener('storage', event => {
  if (event.key === 'lore-ai-demo-v2') {
    ready = false; render(client.state);
    $('status').textContent = 'Another tab changed this demo. Close this tab or refresh it before continuing.';
    $('reload').disabled = true;
  }
});
run(async () => {
  const health = await fetch('/health').then(r => r.json());
  if (health.status !== 'ok') throw new Error('NPC service unavailable');
  $('connection').textContent = '● Service connected'; await client.load();
  if (!client.state.pending && !client.state.memory.hasSpoken) await client.greet();
});
