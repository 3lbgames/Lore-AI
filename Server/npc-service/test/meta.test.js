import test from 'node:test';
import assert from 'node:assert/strict';
import { metaGenerator, metaIdentityVerifier } from '../src/meta.js';

test('Meta adapter sends exact text and permitted context, and extracts only speech', async () => {
  const generate = metaGenerator({ apiKey: 'server-secret', fetchImpl: async (url, options) => {
    assert.equal(url, 'https://api.meta.ai/v1/responses');
    assert.equal(options.headers.authorization, 'Bearer server-secret');
    const body = JSON.parse(options.body);
    assert.equal(body.model, 'muse-spark-1.3'); assert.equal(body.store, false);
    assert.equal(body.input[1].content, '  Hello "friend"\n');
    assert.deepEqual(JSON.parse(body.input[0].content).profile.facts, ['The inn is east.']);
    assert.equal(body.previous_response_id, undefined);
    return Response.json({ status: 'completed', output: [{ type: 'reasoning', content: [{ type: 'output_text', text: 'hidden' }] },
      { type: 'message', role: 'assistant', content: [{ type: 'output_text', text: 'Hello.' }] }] });
  } });
  assert.equal(await generate({ input: '  Hello "friend"\n', profile: { facts: ['The inn is east.'] }, situation: 'Nearby' }), 'Hello.');
});
test('incomplete generation and rate limits are explicit failures', async () => {
  for (const [response, code] of [[Response.json({ status: 'incomplete' }), 'model_incomplete'], [new Response('', { status: 429 }), 'model_rate_limited']]) {
    const generate = metaGenerator({ apiKey: 'secret', fetchImpl: async () => response });
    await assert.rejects(generate({ profile: {}, input: 'Hi' }), { code });
  }
});
test('Meta identity requires positive nonce validation and keeps app secret server-side', async () => {
  let valid = true;
  const verify = metaIdentityVerifier({ appId: '123', appSecret: 'secret', fetchImpl: async (url, options) => {
    assert.equal(url, 'https://graph.oculus.com/user_nonce_validate');
    assert.equal(options.body.get('access_token'), 'OC|123|secret');
    assert.equal(options.body.get('user_id'), '456');
    assert.equal(options.body.get('nonce'), 'proof');
    return Response.json({ is_valid: valid });
  } });
  assert.equal(await verify({ userId: '456', nonce: 'proof' }), '456');
  valid = false;
  await assert.rejects(verify({ userId: '456', nonce: 'proof' }), { status: 401 });
});

test('all acknowledged dialogue is sent once, with roles preserved', async () => {
  const history = [{kind:'player_input',text:'Earlier input'}, {kind:'npc_speech',text:'Earlier answer'}, {kind:'player_input',text:'Current input'}];
  const generate=metaGenerator({apiKey:'secret',fetchImpl:async (_url, options)=>{
    const input=JSON.parse(options.body).input;
    assert.deepEqual(input.slice(1),[{role:'user',content:'Earlier input'},{role:'assistant',content:'Earlier answer'},{role:'user',content:'Current input'}]);
    return Response.json({status:'completed',output:[{type:'message',role:'assistant',content:[{type:'output_text',text:'Reply'}]}]});
  }});
  await generate({history,input:'Current input',profile:{}});
});
test('provider context overflow is explicit without disclosing provider details',async()=>{
  const generate=metaGenerator({apiKey:'secret',fetchImpl:async()=>Response.json({error:{code:'context_length_exceeded',message:'private detail'}},{status:400})});
  await assert.rejects(generate({input:'Hi',profile:{}}),{code:'context_limit',status:422});
});
