import { characters, actionOptions, worldActions, selectGreeting, npcActions } from './profiles.js';

export class DemoClient {
  constructor({storage, request, deliver, changed = () => {}}) {
    this.storage = storage; this.request = request; this.deliver = deliver; this.changed = changed;
    this.state = JSON.parse(storage.getItem('lore-ai-demo-v2') || 'null') || {
      campaignId:crypto.randomUUID(), writerId:crypto.randomUUID(), npcId:'browser-flynn',
      memory:{revision:0, history:[]}, game:{revision:0, values:[]}, pending:null, proposal:null,
    };
    this.sessionId = crypto.randomUUID();
    this.save();
  }
  save() { this.storage.setItem('lore-ai-demo-v2', JSON.stringify(this.state)); this.changed(this.state); }
  get character() { return characters[this.state.npcId]; }
  get values() { return Object.fromEntries(this.state.game.values.map(v => [v.key, v.value])); }
  get actions() {
    return worldActions(this.state.npcId, this.values, this.state.memory.relationship)
      .filter(a => a.actor !== 'npc' || (this.state.proposal?.actionId === a.id && this.state.proposal?.targetId === a.targetId));
  }
  base(turnId = crypto.randomUUID()) {
    return {version:1, sessionId:this.sessionId, interactionId:this.state.interactionId ?? this.sessionId, turnId,
      worldId:'browser-demo', campaignId:this.state.campaignId, npcId:this.state.npcId, writerId:this.state.writerId};
  }
  async load() {
    this.state.memory = await this.request('/v1/memory/load', this.base());
    this.state.game = (await this.request('/v1/game/load', this.base())).gameState;
    this.save();
  }
  async select(npcId) {
    if (this.state.pending) throw new Error('Resume the pending operation first.');
    if (!characters[npcId]) throw new Error('Unknown character.');
    this.state.npcId = npcId; this.state.interactionId = crypto.randomUUID(); this.state.proposal = null; this.save();
    await this.load();
  }
  async newCampaign() {
    if (this.state.pending) throw new Error('Resume the pending operation first.');
    this.state.campaignId = crypto.randomUUID(); this.state.npcId = 'browser-flynn'; this.state.proposal = null;
    this.state.memory = {revision:0,history:[]}; this.state.game = {revision:0,values:[]};
    this.save(); await this.load();
  }
  async turn(input, intent = "Respond to the player's latest utterance in character.") {
    if (this.state.pending) throw new Error('Resume the pending operation first.');
    if (!input.trim() && !intent.trim()) throw new Error('Enter a message.');
    await this.load();
    await this.beginTurn(input, intent);
  }
  async beginTurn(input, intent, options = {}) {
    this.state.proposal = null;
    this.state.pending = {turnId:crypto.randomUUID(), input, intent,
      stage:input ? 'input' : 'generate', ...options};
    this.save(); await this.resume();
  }
  async greet() {
    if (this.state.pending) throw new Error('Resume the pending operation first.');
    await this.load();
    this.state.interactionId = crypto.randomUUID();
    if (this.state.memory.hasSpoken) return this.beginTurn('', 'Greet the returning player briefly in character.', {
      allowActions:false,
      situation:'The player has just approached you again and has not spoken in this encounter. Reflect your relationship and prior conversation naturally. Do not assume a new player utterance or repeat your first introduction. Write a fresh, natural greeting for this encounter. Vary the wording from your recent greetings while staying true to your character, relationship, and what has actually happened.',
      fallbackText:selectGreeting({...this.character, greeting:this.character.returningGreeting ?? 'Good to see you again. What is on your mind?'}, this.state.memory.relationship),
    });
    this.state.proposal = null;
    this.state.pending = {turnId:crypto.randomUUID(), input:'', stage:'deliver', text:selectGreeting(this.character, this.state.memory.relationship)};
    this.save(); await this.resume();
  }
  async act(id, delivery = 'completed') {
    if (this.state.pending) throw new Error('Resume the pending operation first.');
    await this.load();
    const action = this.actions.find(a => a.id === id);
    if (!action || !['completed','failed'].includes(delivery)) throw new Error('That action is no longer available. Reload state.');
    this.state.pending = {turnId:crypto.randomUUID(), stage:delivery === 'completed' && action.key ? 'game' : 'outcome',
      action, delivery, expected:this.values[action.key] ?? ''};
    this.save(); await this.resume();
  }
  async send(route, body) {
    const p = this.state.pending;
    if (!p.request) { p.request = {route, body}; this.save(); }
    try {
      const result = await this.request(p.request.route, p.request.body);
      delete p.request;
      return result;
    } catch (error) {
      // An explicit conflict is a rejection, unlike an ambiguous network failure.
      // Refresh revisions before retrying; never replay a stale world transition.
      if (error.code === 'revision_conflict') {
        delete p.request;
        await this.load();
        if (p.stage === 'game' && (this.values[p.action.key] ?? '') !== p.expected) {
          this.state.pending = null; this.state.proposal = null;
        }
        this.save();
      } else if (error.code === 'state_conflict' && p.stage === 'game') {
        this.state.pending = null; this.state.proposal = null; this.save();
        await this.load();
      }
      throw error;
    }
  }
  async commit(kind, text, delivery, evidenceKey, suffix = kind) {
    const p = this.state.pending;
    const result = await this.send('/v1/memory/commit', {...this.base(p.turnId), expectedRevision:this.state.memory.revision,
      relationship:this.character.profile.relationship,
      event:{eventId:p.turnId + '-' + suffix, turnId:p.turnId, kind, text, delivery, ...(evidenceKey ? {evidenceKey} : {})}});
    this.state.memory.revision = result.revision;
    if (result.relationship) this.state.memory.relationship = result.relationship;
  }
  async generate() {
    const p = this.state.pending;
    if (!p.offeredActions) {
      p.offeredActions = actionOptions(this.state.npcId, this.values, this.state.memory.relationship);
      this.save();
    }
    let result;
    try {
      result = await this.send('/v1/responses', {...this.base(p.turnId), expectedRevision:this.state.memory.revision,
        input:p.input, npcInitiated:!p.input, intent:p.intent, profile:this.character.profile,
        situation:(p.situation ?? '') + '\nBrowser physical simulation: cup_location=inventory means the cup is held, not on the pickup table; first_delivery=completed means Osric has it. Accepted campaign state: ' + JSON.stringify(this.values),
        actions:p.offeredActions});
    } catch (error) {
      if (p.worldEvent && !p.generationRetried) {
        p.generationRetried = true; this.save();
        await new Promise(resolve => setTimeout(resolve, 500));
        return this.generate();
      }
      const fallback = p.firstDraft ?? p.fallbackText ?? (p.input ? 'I cannot talk right now. Try me again in a moment.' : undefined);
      if (fallback === undefined) throw error;
      delete p.request;
      // A lost generation acknowledgement may have accepted relationship evidence.
      await this.load();
      p.text = fallback; this.state.proposal = null; p.stage = 'deliver'; this.save();
      return;
    }
    this.state.memory.revision = result.revision;
    this.state.memory.relationship = result.relationship;
    const available = actionOptions(this.state.npcId, this.values, this.state.memory.relationship);
    if (p.input && !p.reconsidered && !result.actionProposal && available.some(a =>
      !p.offeredActions.some(before => before.id === a.id && before.targetId === a.targetId))) {
      p.reconsidered = true; p.firstDraft = result.text; p.offeredActions = available;
      p.intent += '\nReconsider this same player utterance using the accepted relationship and newly available actions. The earlier draft was not delivered. Choose the final reply and action together; this is not a new player turn.';
      this.save();
      return this.generate();
    }
    this.state.proposal = p.allowActions === false ? null : result.actionProposal;
    p.text = result.text; p.stage = 'npc-action'; this.save();
  }
  async resume() {
    const p = this.state.pending;
    if (!p) return this.load();
    if (p.stage === 'input') {
      await this.commit('player_input', p.input, 'completed');
      p.stage = 'generate'; this.save();
    }
    if (p.stage === 'generate') await this.generate();
    if (p.stage === 'npc-action') {
      await this.load();
      const proposal = this.state.proposal;
      const action = proposal && npcActions(this.state.npcId, this.values, this.state.memory.relationship)
        .find(a => a.id === proposal.actionId && a.targetId === proposal.targetId);
      const offered = proposal && p.offeredActions?.some(a => a.id === proposal.actionId && a.targetId === proposal.targetId);
      if (proposal && p.allowActions !== false) {
        p.afterAction = p.speechCommitted ? 'done' : 'deliver';
        p.action = action ?? {id:proposal.actionId, targetId:proposal.targetId, actor:'npc', label:proposal.actionId};
        p.delivery = offered && action ? 'completed' : 'failed';
        p.actionError = offered ? 'requirements_not_met' : 'action_not_offered';
        p.expected = this.values[p.action.key] ?? '';
        p.stage = p.delivery === 'completed' && p.action.key ? 'game' : 'outcome';
      } else p.stage = p.speechCommitted ? 'done' : 'deliver';
      this.save();
    }
    if (p.stage === 'game') {
      const result = await this.send('/v1/game/commit', {...this.base(p.turnId), expectedRevision:this.state.game.revision,
        mutation:{eventId:p.turnId + '-world', key:p.action.key, expected:p.expected, value:p.action.value}});
      this.state.game = result.gameState;
      p.stage = p.action.completionText ? 'effect-outcome' : 'outcome'; this.save();
    }
    if (p.stage === 'effect-outcome') {
      await this.commit('action_outcome', p.action.completionText, p.delivery, p.action.evidenceKey, 'quest-outcome');
      p.stage = 'outcome'; this.save();
    }
    if (p.stage === 'outcome') {
      const text = p.action.actor === 'npc'
        ? `Action ${p.action.id} on ${p.action.targetId} ${p.delivery === 'completed' ? 'completed.' : 'failed: ' + p.actionError}`
        : p.delivery === 'completed' ? p.action.text : `The browser simulation could not execute ${p.action.label}. No world change occurred.`;
      if (p.action.recordMemory !== false) await this.commit('action_outcome', text, p.delivery, p.action.evidenceKey);
      this.state.proposal = null;
      p.stage = p.delivery === 'completed' ? 'notifications' : (p.afterAction ?? 'done'); this.save();
    }
    if (p.stage === 'notifications') {
      const notifications = p.action.notifications ?? [];
      for (let i = p.notificationIndex ?? 0; i < notifications.length; i++) {
        const notice = notifications[i];
        const base = {...this.base(p.turnId), npcId:notice.npcId};
        const memory = await this.request('/v1/memory/load', base);
        await this.send('/v1/memory/commit', {...base, expectedRevision:memory.revision,
          relationship:characters[notice.npcId].profile.relationship,
          event:{eventId:p.turnId + '-notice-' + notice.npcId, turnId:p.turnId,
            kind:'action_outcome', text:notice.text, delivery:'completed'}});
        p.notificationIndex = i + 1; this.save();
      }
      if (p.action.reactionIntent || p.action.reactionText) {
        this.state.pending = {turnId:crypto.randomUUID(), input:'', intent:p.action.reactionIntent ?? '',
          situation:p.action.text, worldEvent:!!p.action.reactionIntent, allowActions:!p.action.reactionText,
          text:p.action.reactionText, stage:p.action.reactionText ? 'deliver' : 'generate'};
        this.save();
        return this.resume();
      }
      p.stage = p.afterAction ?? 'done'; this.save();
    }
    if (p.stage === 'deliver') {
      if (!p.text?.trim()) p.stage = 'done';
      else {
        p.delivery = 'interrupted'; p.stage = 'speech'; this.save();
        p.delivery = await this.deliver(p.text);
      }
      this.save();
    }
    if (p.stage === 'speech') {
      await this.commit('npc_speech', p.text, p.delivery);
      p.speechCommitted = true; p.stage = 'done'; this.save();
    }
    this.state.pending = null; this.save(); await this.load();
  }
}
