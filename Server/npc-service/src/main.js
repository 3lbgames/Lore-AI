import { readFileSync } from 'node:fs';
import { createService } from './service.js';
import { metaGenerator, metaIdentityVerifier } from './meta.js';

import { PostgresMemoryStore } from './memory.js';

if (!process.env.DATABASE_URL) throw new Error('DATABASE_URL is required');
const memoryStore = new PostgresMemoryStore(process.env.DATABASE_URL);
try { await memoryStore.migrate(); } catch { console.error('PostgreSQL migration failed'); process.exit(1); }
const authMode = process.env.AUTH_MODE ?? 'meta';
const host = process.env.HOST ?? '127.0.0.1';
if (authMode === 'local' && host !== '127.0.0.1') throw new Error('Local authentication requires HOST=127.0.0.1; use Meta authentication for LAN.');
if (authMode === 'meta' && (!process.env.META_APP_ID || !process.env.META_APP_SECRET)) throw new Error('META_APP_ID and META_APP_SECRET are required for Meta authentication.');
const grants = authMode === 'local'
  ? JSON.parse(readFileSync(new URL('../scopes.local.json', import.meta.url), 'utf8')) : undefined;
const server = createService({ memoryStore, authMode, localToken: process.env.LOCAL_ACCESS_TOKEN, grants,
  generate: metaGenerator({ apiKey: process.env.MODEL_API_KEY, reasoning: process.env.NPC_MODEL_REASONING ?? 'minimal' }),
  verifyIdentity: metaIdentityVerifier({ appId: process.env.META_APP_ID, appSecret: process.env.META_APP_SECRET }) });
server.listen(Number(process.env.PORT ?? 8787), host, () => console.log(`NPC service listening on http://${host}:${server.address().port}`));

for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => { server.close(async () => { await memoryStore.close(); process.exit(0); }); });
