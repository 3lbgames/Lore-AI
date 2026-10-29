CREATE TABLE campaign_game_states (
  player_id text NOT NULL, world_id text NOT NULL, campaign_id text NOT NULL,
  revision integer NOT NULL DEFAULT 0,
  state_values jsonb NOT NULL DEFAULT '[]'::jsonb,
  PRIMARY KEY(player_id,world_id,campaign_id)
);
CREATE TABLE campaign_game_events (
  player_id text NOT NULL, world_id text NOT NULL, campaign_id text NOT NULL,
  event_id text NOT NULL, payload jsonb NOT NULL,
  PRIMARY KEY(player_id,world_id,campaign_id,event_id),
  FOREIGN KEY(player_id,world_id,campaign_id) REFERENCES campaign_game_states
);
