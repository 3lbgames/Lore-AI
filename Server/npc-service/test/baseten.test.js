import test from 'node:test';
import assert from 'node:assert/strict';
import { modelGenerator } from '../src/generation.js';
const basetenGenerator = options => modelGenerator({ ...options, provider: 'baseten' });

const request = { input: 'Please open it', profile: {}, actions: [{ id: 'open', targetId: 'gate' }] };
const reply = { speech: 'I will try.', relationshipProposal: { ruleId: null, evidence: null }, actionProposal: { actionId: 'open', targetId: 'gate' } };
const completion = value => Response.json({ status: 'completed', output: [
  { type: 'reasoning', content: [{ type: 'output_text', text: 'private reasoning' }] },
  { type: 'message', role: 'assistant', content: [{ type: 'output_text', text: typeof value === 'string' ? value : JSON.stringify(value) }] }
] });

test('Baseten sends the exact model and prompt schema without unsupported controls', async () => {
  const signal = new AbortController().signal;
  const generate = basetenGenerator({ apiKey: 'baseten-secret', fetchImpl: async (url, options) => {
    assert.equal(url, 'https://inference.baseten.co/v1/responses');
    assert.equal(options.headers.authorization, 'Bearer baseten-secret');
    assert.equal(options.signal, signal);
    const body = JSON.parse(options.body);
    assert.equal(body.model, 'meta-models/muse-spark-open-1026');
    assert.equal(body.stream, false);
    assert.equal(body.store, false);
    assert.equal(body.text, undefined);
    assert.equal(body.reasoning, undefined);
    assert.match(body.instructions, /JSON Schema/);
    assert.match(body.instructions, /"enum":\["open"\]/);
    assert.equal(body.input[1].content, request.input);
    return completion(reply);
  } });
  assert.deepEqual(await generate(request, signal), reply);
});

test('Baseten rejects malformed JSON and invalid gameplay proposals', async () => {
  for (const value of ['not JSON', { ...reply, actionProposal: { actionId: 'steal', targetId: 'gate' } },
    { ...reply, relationshipProposal: { ruleId: 'unknown', evidence: request.input } }]) {
    const generate = basetenGenerator({ apiKey: 'secret', fetchImpl: async () => completion(value) });
    await assert.rejects(generate(request), error => ['invalid_model_response', 'invalid_action_proposal'].includes(error.code));
  }
});

test('Baseten preserves missing-key, rate-limit, context-limit and incomplete errors', async () => {
  await assert.rejects(basetenGenerator({})(request), { code: 'model_key_missing' });
  for (const [response, code] of [
    [new Response('', { status: 429 }), 'model_rate_limited'],
    [new Response('', { status: 529 }), 'model_unavailable'],
    [Response.json({ error: { code: 'context_length_exceeded' } }, { status: 400 }), 'context_limit'],
    [Response.json({ status: 'incomplete' }), 'model_incomplete']
  ]) {
    await assert.rejects(basetenGenerator({ apiKey: 'secret', fetchImpl: async () => response })(request), { code });
  }
});
