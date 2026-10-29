import { ServiceError } from './service.js';
export function validateActionProposal(proposal) {
  if (proposal !== null) throw new ServiceError(502,'invalid_action_proposal');
}
export function validateModelReply(reply,input,rules) {
  const invalid = () => { throw new ServiceError(502,'invalid_model_response'); };
  if (!reply || Array.isArray(reply) || Object.keys(reply).length !== 3 || typeof reply.speech !== 'string' || !reply.speech.trim() || reply.speech.length > 12000 || !Object.hasOwn(reply,'relationshipProposal')) invalid();
  validateActionProposal(reply.actionProposal);
  const p = reply.relationshipProposal;
  if (!p || Array.isArray(p) || Object.keys(p).length !== 2 || !Object.hasOwn(p,'ruleId') || !Object.hasOwn(p,'evidence')) invalid();
  if (p.ruleId === null && p.evidence === null) return;
  if (!rules.some(r=>r.id === p.ruleId) || typeof p.evidence !== 'string' || !p.evidence.trim() || !input.includes(p.evidence)) invalid();
}
