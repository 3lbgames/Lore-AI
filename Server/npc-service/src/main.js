import { readFileSync } from 'node:fs';
import { createService } from './service.js';
import { metaGenerator, metaIdentityVerifier } from './meta.js';

const authMode = process.env.AUTH_MODE ?? 'meta';
const host = process.env.HOST ?? '127.0.0.1';
if (authMode === 'local' && host !== '127.0.0.1') throw new Error('Local authentication requires HOST=127.0.0.1; use Meta authentication for LAN.');
if (authMode === 'meta' && (!process.env.META_APP_ID || !process.env.META_APP_SECRET)) throw new Error('META_APP_ID and META_APP_SECRET are required for Meta authentication.');
const grants = JSON.parse(readFileSync(new URL('../scopes.local.json', import.meta.url), 'utf8'));
const server = createService({ authMode, localToken: process.env.LOCAL_ACCESS_TOKEN, grants,
  generate: metaGenerator({ apiKey: process.env.MODEL_API_KEY }),
  verifyIdentity: metaIdentityVerifier({ appId: process.env.META_APP_ID, appSecret: process.env.META_APP_SECRET }) });
server.listen(Number(process.env.PORT ?? 8787), host, () => console.log(`NPC service listening on http://${host}:${server.address().port}`));
