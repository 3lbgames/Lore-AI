import { modelGenerator } from '../src/generation.js';

if (!process.env.BASETEN_API_KEY) throw new Error('Set BASETEN_API_KEY in .env before running the smoke test.');
const generate = modelGenerator({ provider: 'baseten', apiKey: process.env.BASETEN_API_KEY,
  model: 'meta-models/muse-spark-open-1026' });
const profile = { name: 'Gatekeeper', personality: 'Friendly and practical', facts: ['The inn is east of the gate.'] };
const cases = [
  { name: 'Directions', input: 'Where is the inn?', profile },
  { name: 'Permitted action', input: 'Please open the gate.', profile,
    actions: [{ id: 'open_gate', targetId: 'village_gate', description: 'Attempt to open the village gate.' }] },
  { name: 'Failed action follow-up', input: '', npcInitiated: true, profile,
    intent: 'Explain the confirmed outcome briefly.',
    history: [{ kind: 'action_outcome', text: 'The gate was jammed and could not be opened.', delivery: 'failed' }] }
];

for (const { name, ...request } of cases) {
  const start = performance.now();
  try {
    const reply = await generate(request, AbortSignal.timeout(30000));
    console.log(JSON.stringify({ case: name, elapsedMs: Math.round(performance.now() - start), ...reply }));
  } catch (error) {
    console.error(JSON.stringify({ case: name, elapsedMs: Math.round(performance.now() - start), error: error.code ?? error.name }));
    process.exitCode = 1;
    break;
  }
}
