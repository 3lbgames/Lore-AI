import { createService } from './service.js';
import { metaIdentityVerifier } from './meta.js';
import { modelGenerator } from './generation.js';

import { PostgresMemoryStore } from './memory.js';

const provider = process.env.NPC_MODEL_PROVIDER ?? 'meta';
const generate = modelGenerator({
  provider,
  apiKey: provider === 'baseten' ? process.env.BASETEN_API_KEY : process.env.MODEL_API_KEY,
  model: process.env.NPC_MODEL || undefined,
  reasoning: process.env.NPC_MODEL_REASONING ?? 'minimal'
});
if (!process.env.DATABASE_URL) throw new Error('DATABASE_URL is required');
const memoryStore = new PostgresMemoryStore(process.env.DATABASE_URL);
try { await memoryStore.migrate(); } catch { console.error('PostgreSQL migration failed'); process.exit(1); }
const authMode = process.env.AUTH_MODE ?? 'meta';
const host = process.env.HOST ?? '127.0.0.1';
if (authMode === 'local' && host !== '127.0.0.1') throw new Error('Local authentication requires HOST=127.0.0.1; use Meta authentication for LAN.');
if (authMode === 'meta' && (!process.env.META_APP_ID || !process.env.META_APP_SECRET)) throw new Error('META_APP_ID and META_APP_SECRET are required for Meta authentication.');
const server = createService({ memoryStore, authMode, localToken: process.env.LOCAL_ACCESS_TOKEN,
  generate,
  verifyIdentity: metaIdentityVerifier({ appId: process.env.META_APP_ID, appSecret: process.env.META_APP_SECRET }) });
server.listen(Number(process.env.PORT ?? 8787), host, () => console.log(`NPC service listening on http://${host}:${server.address().port}`));

for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => { server.close(async () => { await memoryStore.close(); process.exit(0); }); });
