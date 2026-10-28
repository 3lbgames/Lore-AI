CREATE TABLE campaign_writers (
  player_id text NOT NULL, world_id text NOT NULL, campaign_id text NOT NULL,
  writer_id text NOT NULL, lease_until timestamptz NOT NULL,
  PRIMARY KEY(player_id, world_id, campaign_id)
);
CREATE TABLE conversation_streams (
  player_id text NOT NULL, world_id text NOT NULL, campaign_id text NOT NULL, npc_id text NOT NULL,
  revision integer NOT NULL DEFAULT 0,
  PRIMARY KEY(player_id,world_id,campaign_id,npc_id)
);
CREATE TABLE conversation_events (
  player_id text NOT NULL, world_id text NOT NULL, campaign_id text NOT NULL, npc_id text NOT NULL,
  event_id text NOT NULL, revision integer NOT NULL, payload jsonb NOT NULL,
  PRIMARY KEY(player_id,world_id,campaign_id,npc_id,event_id),
  UNIQUE(player_id,world_id,campaign_id,npc_id,revision),
  FOREIGN KEY(player_id,world_id,campaign_id,npc_id) REFERENCES conversation_streams
);
