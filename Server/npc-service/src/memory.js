import { readdir, readFile } from 'node:fs/promises';
import { isDeepStrictEqual } from 'node:util';
import pg from 'pg';
import { ServiceError } from './service.js';

// IVillagerMemoryStore: load(scope, { includeHistory = true }), commit(scope, expectedRevision, event).
// scope = authenticated playerId + worldId/campaignId/npcId/writerId.
// Both methods renew the campaign writer lease. Commit returns the immutable
// receipt revision; load returns the latest revision, hasSpoken and ordered events.
export class PostgresMemoryStore {
  constructor(connectionString) {
    this.pool = new pg.Pool({ connectionString, connectionTimeoutMillis: 5000, statement_timeout: 10000 });
    this.pool.on('error', () => {}); // Requests report bounded storage_unavailable; never log credentials.
  }
  async migrate() {
    const client = await this.pool.connect();
    try {
      await client.query('BEGIN');
      await client.query('SELECT pg_advisory_xact_lock(830031)');
      await client.query('CREATE TABLE IF NOT EXISTS npc_schema_migrations (name text PRIMARY KEY)');
      const directory = new URL('../migrations/', import.meta.url);
      for (const name of (await readdir(directory)).filter(n => /^\d+.*\.sql$/.test(n)).sort()) {
        if ((await client.query('SELECT 1 FROM npc_schema_migrations WHERE name=$1', [name])).rowCount) continue;
        await client.query(await readFile(new URL(name, directory), 'utf8'));
        await client.query('INSERT INTO npc_schema_migrations VALUES ($1)', [name]);
      }
      await client.query('COMMIT');
    } catch (error) { await client.query('ROLLBACK'); throw error; }
    finally { client.release(); }
  }
  async transaction(scope, operation) {
    let client;
    try {
      client = await this.pool.connect();
      await client.query('BEGIN');
      const branch = [scope.playerId, scope.worldId, scope.campaignId];
      const lease = await client.query(`INSERT INTO campaign_writers VALUES ($1,$2,$3,$4,clock_timestamp()+interval '120 seconds')
        ON CONFLICT (player_id,world_id,campaign_id) DO UPDATE SET writer_id=$4,lease_until=clock_timestamp()+interval '120 seconds'
        WHERE campaign_writers.writer_id=$4 OR campaign_writers.lease_until<=clock_timestamp() RETURNING writer_id`, [...branch, scope.writerId]);
      if (!lease.rowCount) throw new ServiceError(409, 'writer_conflict');
      const key = [...branch, scope.npcId];
      await client.query('INSERT INTO conversation_streams(player_id,world_id,campaign_id,npc_id) VALUES($1,$2,$3,$4) ON CONFLICT DO NOTHING', key);
      const { rows: [stream] } = await client.query('SELECT revision FROM conversation_streams WHERE player_id=$1 AND world_id=$2 AND campaign_id=$3 AND npc_id=$4 FOR UPDATE', key);
      const result = await operation(client, key, stream.revision);
      await client.query('COMMIT');
      return result;
    } catch (error) {
      if (client) await client.query('ROLLBACK').catch(() => {});
      throw error instanceof ServiceError ? error : new ServiceError(503, 'storage_unavailable');
    } finally { client?.release(); }
  }
  load(scope, { includeHistory = true } = {}) {
    return this.transaction(scope, async (client, key, revision) => {
      if (!includeHistory) return { revision };
      const { rows } = await client.query('SELECT payload FROM conversation_events WHERE player_id=$1 AND world_id=$2 AND campaign_id=$3 AND npc_id=$4 ORDER BY revision', key);
      const history = rows.map(row => row.payload);
      return { revision, hasSpoken: history.some(event => event.kind === 'player_input'), history };
    });
  }
  commit(scope, expectedRevision, event) {
    return this.transaction(scope, async (client, key, revision) => {
      const { rows: [existing] } = await client.query('SELECT revision,payload FROM conversation_events WHERE player_id=$1 AND world_id=$2 AND campaign_id=$3 AND npc_id=$4 AND event_id=$5', [...key, event.eventId]);
      if (existing) {
        if (!isDeepStrictEqual(existing.payload, event)) throw new ServiceError(409, 'event_conflict');
        return { revision: existing.revision };
      }
      if (revision !== expectedRevision) throw new ServiceError(409, 'revision_conflict');
      const next = revision + 1;
      await client.query('INSERT INTO conversation_events VALUES ($1,$2,$3,$4,$5,$6,$7)', [...key,event.eventId,next,event]);
      await client.query('UPDATE conversation_streams SET revision=$5 WHERE player_id=$1 AND world_id=$2 AND campaign_id=$3 AND npc_id=$4', [...key,next]);
      return { revision: next };
    });
  }
  close() { return this.pool.end(); }
}
