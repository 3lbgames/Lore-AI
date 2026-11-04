import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { createDemoServer } from '../demo/server.js';

test('demo serves only its files and proxies same-origin API calls without exposing credentials', async t => {
  const calls = [];
  const upstream = http.createServer(async (req, res) => {
    let body = ''; for await (const chunk of req) body += chunk;
    calls.push({url:req.url, token:req.headers.authorization, body});
    res.writeHead(409, {'content-type':'application/json'});
    res.end(JSON.stringify({error:'revision_conflict'}));
  });
  await new Promise(r => upstream.listen(0, '127.0.0.1', r));
  const server = createDemoServer({upstream:`http://127.0.0.1:${upstream.address().port}`, token:'private-test-token'});
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  t.after(() => { for (const s of [server, upstream]) { s.closeAllConnections(); s.close(); } });
  const origin = `http://127.0.0.1:${server.address().port}`;
  const page = await fetch(origin);
  assert.equal(page.status, 200);
  assert.match(await page.text(), /LORE/);
  assert.equal((await fetch(origin + '/.env')).status, 404);
  const badHost = await new Promise(resolve => {
    http.get(origin, {headers:{host:'evil.example'}}, response => { response.resume(); resolve(response.statusCode); });
  });
  assert.equal(badHost, 403);
  const post = headers => fetch(origin + '/v1/memory/load', {method:'POST',headers,body:'{"version":1}'});
  assert.equal((await post({'content-type':'application/json',origin:'https://evil.example'})).status, 403);
  assert.equal((await post({'content-type':'application/json'})).status, 403);
  const result = await post({'content-type':'application/json',origin});
  assert.equal(result.status, 409);
  assert.deepEqual(await result.json(), {error:'revision_conflict'});
  assert.deepEqual(calls, [{url:'/v1/memory/load',token:'Bearer private-test-token',body:'{"version":1}'}]);
});

test('dictation transcribes same-origin PCM on the server instead of browser speech recognition', async t => {
  const recordings = [];
  const server = createDemoServer({token:'private-test-token', transcribe:async pcm => {
    recordings.push(Buffer.from(pcm)); return 'Please move the wasps gently.';
  }});
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  t.after(() => { server.closeAllConnections(); server.close(); });
  const origin = `http://127.0.0.1:${server.address().port}`;
  const pcm = Buffer.alloc(4800);
  const post = (body, source = origin) => fetch(origin + '/transcribe', {
    method:'POST', headers:{origin:source,'content-type':'application/octet-stream'}, body,
  });
  assert.equal((await post(pcm, 'https://other.example')).status, 403);
  assert.equal((await post(Buffer.alloc(3))).status, 400);
  assert.equal((await post(Buffer.alloc(0))).status, 400);
  assert.equal((await post(Buffer.alloc(1440002))).status, 413);
  const response = await post(pcm);
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), {text:'Please move the wasps gently.'});
  assert.deepEqual(recordings, [pcm]);
});
