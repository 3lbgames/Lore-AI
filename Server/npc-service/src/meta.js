import { ServiceError } from './service.js';
import { readFileSync } from 'node:fs';
import { validateModelReply } from './model-reply.js';
const instructions = readFileSync(new URL('./prompts/interaction.txt', import.meta.url), 'utf8');

export function metaIdentityVerifier({ appId, appSecret, fetchImpl = fetch }) {
  return async (identity, signal) => {
    if (!identity || !/^\d{1,30}$/.test(identity.userId) || typeof identity.nonce !== 'string' || !identity.nonce || identity.nonce.length > 4096)
      throw new ServiceError(401, 'invalid_identity');
    const response = await fetchImpl('https://graph.oculus.com/user_nonce_validate', {
      method: 'POST', signal,
      body: new URLSearchParams({ access_token: `OC|${appId}|${appSecret}`, user_id: identity.userId, nonce: identity.nonce })
    });
    if (!response.ok) throw new ServiceError(401, 'identity_rejected');
    const result = await response.json();
    if (result.is_valid !== true) throw new ServiceError(401, 'identity_rejected');
    return identity.userId;
  };
}
function metaCompletion({ apiKey, model = 'muse-spark-1.3', fetchImpl = fetch, reasoning = 'minimal' }) {
  if (!['minimal','low','medium','high','xhigh','max'].includes(reasoning)) throw new Error('Invalid NPC_MODEL_REASONING');
  return async (instructions, input, schema, signal) => {
    if (!apiKey) throw new ServiceError(503, 'model_key_missing');
    const response = await fetchImpl('https://api.meta.ai/v1/responses', {
      method: 'POST', signal,
      headers: { authorization: `Bearer ${apiKey}`, 'content-type': 'application/json' },
      body: JSON.stringify({ model, stream: false, store: false, reasoning: { effort: reasoning }, text: { format: { type: 'json_schema', name: 'npc_interaction', strict: true, schema } }, instructions, input })
    });
    if (!response.ok && [400, 413].includes(response.status)) {
      const failure = await response.json().catch(() => ({}));
      if (/context|token.*limit|too.*long/i.test(String(failure.error?.code ?? '') + ' ' + String(failure.error?.message ?? ''))) throw new ServiceError(422, 'context_limit');
    }
    if (!response.ok) throw new ServiceError(response.status === 429 ? 429 : 502, response.status === 429 ? 'model_rate_limited' : 'model_unavailable');
    const result = await response.json();
    if (result.status !== 'completed') throw new ServiceError(502, 'model_incomplete');
    return (result.output ?? []).filter(item => item.type === 'message' && item.role === 'assistant')
      .flatMap(item => item.content ?? []).filter(item => item.type === 'output_text').map(item => item.text).join('');
  };
}

function dialogueHistory(request) {
  return (request.history ?? (request.npcInitiated ? [] : [{ kind: 'player_input', text: request.input }])).map(event =>
    event.kind === 'action_outcome' ? { role: 'developer', content: JSON.stringify(event) } :
    { role: event.kind === 'player_input' ? 'user' : 'assistant', content: event.text });
}

function dialogueProfile(profile) {
  const { relationship, ...content } = profile;
  return content;
}

export function metaGenerator(options) {
  const complete = metaCompletion(options);
  return async (request, signal) => {
    const rules = request.eligibleRules ?? [];
    const actions = request.actions ?? [];
    const actionSchema = actions.length ? {anyOf:[{type:'null'},{type:'object',properties:{
      actionId:{type:'string',enum:actions.map(a=>a.id)}, targetId:{type:'string',enum:[...new Set(actions.map(a=>a.targetId))]}
    },required:['actionId','targetId'],additionalProperties:false}]} : {type:'null'};
    const schema = {type:'object',properties:{speech:{type:'string'},relationshipProposal:{type:'object',properties:{ruleId:{type:['string','null'],enum:[...rules.map(r=>r.id),null]},evidence:{type:['string','null']}},required:['ruleId','evidence'],additionalProperties:false},actionProposal:actionSchema},required:['speech','relationshipProposal','actionProposal'],additionalProperties:false};
    const output = await complete(instructions,[
      {role:'developer',content:JSON.stringify({profile:dialogueProfile(request.profile),situation:request.situation,intent:request.intent,npcInitiated:request.npcInitiated===true,actions,relationship:{categories:(request.profile.relationship?.categories ?? []).map(({id,description,expression,initial,min,max})=>({id,description,expression,initial,min,max})),values:request.relationship?.values ?? [],experiences:request.relationship?.experiences ?? []},rules:rules.map(({id,condition})=>({id,condition}))})},
      ...dialogueHistory(request)
    ],schema,signal);
    let reply;
    try { reply=JSON.parse(output); } catch { throw new ServiceError(502,'invalid_model_response'); }
    validateModelReply(reply,request.input,rules,actions);
    return reply;
  };
}
