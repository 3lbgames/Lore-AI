const bounded = n => Number.isSafeInteger(n) && Math.abs(n) <= 10000;
const text = v => typeof v === 'string' && v.trim().length > 0 && v.length <= 2000;
const id = v => text(v) && v.length <= 128;
export function validateRelationship(config) {
  if (config == null) return;
  const invalid = () => { throw new Error('invalid_relationship'); };
  if (!Array.isArray(config.categories) || !config.categories.length || config.categories.length > 50 || !Array.isArray(config.rules) || config.rules.length > 50) invalid();
  const categories = new Set();
  for (const c of config.categories) {
    if (!c || !id(c.id) || categories.has(c.id) || !text(c.description) || !text(c.expression) || ![c.initial,c.min,c.max].every(bounded) || c.min > c.initial || c.initial > c.max) invalid();
    categories.add(c.id);
  }
  const rules = new Set();
  for (const r of config.rules) {
    if (!r || !id(r.id) || rules.has(r.id) || !['dialogue','action'].includes(r.source) || !text(r.condition) || !text(r.meaning) || (r.source === 'action' && !id(r.evidenceKey)) || !Array.isArray(r.effects) || !r.effects.length || r.effects.length > categories.size) invalid();
    rules.add(r.id);
    const effects = new Set();
    for (const e of r.effects) {
      if (!e || !categories.has(e.category) || effects.has(e.category) || !bounded(e.delta)) invalid();
      effects.add(e.category);
    }
    if (r.repeat != null && (!Number.isSafeInteger(r.repeat.maxApplications) || r.repeat.maxApplications < 0 || r.repeat.maxApplications > 10000 || !Number.isSafeInteger(r.repeat.cooldownSeconds) || r.repeat.cooldownSeconds < 0 || r.repeat.cooldownSeconds > 31536000)) invalid();
  }
}
export function projectRelationship(config, previous) {
  validateRelationship(config);
  if (!config) return previous;
  const state = structuredClone(previous ?? {values:[],ruleUsage:[],evaluatedEventIds:[],experiences:[]});
  for (const c of config.categories) if (!state.values.some(v => v.category === c.id)) state.values.push({category:c.id,value:c.initial});
  return state;
}
function available(rule, state, now) {
  const usage = state?.ruleUsage.find(u => u.ruleId === rule.id);
  const repeat = rule.repeat ?? {maxApplications:1,cooldownSeconds:0};
  return (!repeat.maxApplications || (usage?.applications ?? 0) < repeat.maxApplications) &&
    (!usage || !repeat.cooldownSeconds || now - Date.parse(usage.lastAppliedAt) >= repeat.cooldownSeconds * 1000);
}
export function eligibleDialogueRules(config, state, now = Date.now()) {
  return (config?.rules ?? []).filter(r => r.source === 'dialogue' && available(r,state,now));
}
export function assessRelationship(config, previous, event, proposal = null, now = Date.now()) {
  const state = projectRelationship(config,previous);
  if (!config || state.evaluatedEventIds.includes(event.eventId)) return state;
  state.evaluatedEventIds.push(event.eventId);
  let rule;
  if (event.delivery === 'completed' && event.kind === 'action_outcome' && event.evidenceKey)
    rule = config.rules.find(r => r.source === 'action' && r.evidenceKey === event.evidenceKey);
  if (event.delivery === 'completed' && event.kind === 'player_input' && typeof proposal?.evidence === 'string' && proposal.evidence.trim() && event.text.includes(proposal.evidence))
    rule = config.rules.find(r => r.source === 'dialogue' && r.id === proposal.ruleId);
  if (!rule || !available(rule,state,now)) return state;
  const effects = rule.effects.map(effect => {
    const category = config.categories.find(c => c.id === effect.category);
    const value = state.values.find(v => v.category === effect.category);
    const before = value.value;
    value.value = Math.max(category.min,Math.min(category.max,before + effect.delta));
    return {category:category.id,delta:value.value-before};
  });
  const occurredAt = new Date(now).toISOString();
  let usage = state.ruleUsage.find(u => u.ruleId === rule.id);
  if (!usage) { usage={ruleId:rule.id,applications:0,lastAppliedAt:null}; state.ruleUsage.push(usage); }
  usage.applications++; usage.lastAppliedAt=occurredAt;
  state.experiences.push({ruleId:rule.id,sourceEventId:event.eventId,source:rule.source,evidence:rule.source==='dialogue'?proposal.evidence:event.text,meaning:rule.meaning,effects,occurredAt});
  return state;
}
