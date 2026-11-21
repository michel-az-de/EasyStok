import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer, request } from 'node:http';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';
import { criarApp, hashToken } from '../src/diag/http.mjs';
import { criarServidorDiag } from '../src/diag/fabrica.mjs';

const TOKEN = 'tok-valido-0123456789abcdef0123456789abcdef';
const VENCIDO = 'tok-vencido-0123456789abcdef0123456789abcd';

function arquivoTokens(linhas) {
  const dir = mkdtempSync(join(tmpdir(), 'mcp-tok-'));
  const arq = join(dir, 'tokens');
  writeFileSync(arq, linhas.join('\n') + '\n');
  return arq;
}

async function subir(opcoes = {}) {
  const auditoria = [];
  const tokens = opcoes.tokens ?? arquivoTokens([
    '# pessoa:sha256:validade',
    `teste:${hashToken(TOKEN)}:2099-12-31`,
    `antigo:${hashToken(VENCIDO)}:2020-01-01`,
  ]);
  const app = criarApp({
    hosts: ['127.0.0.1'], tokens,
    criarServidor: o => criarServidorDiag({ ...o, executar: async cmd => (cmd === 'containers' ? 'ez-api|Up 2 hours|img' : 'linha') }),
    auditar: l => auditoria.push(l),
    ...opcoes,
  });
  const s = createServer(app);
  await new Promise(r => s.listen(0, '127.0.0.1', r));
  return { s, porta: s.address().port, auditoria, tokens };
}

const PING = JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'ping' });
function pedir(porta, { metodo = 'POST', caminho = '/diag', headers = {}, corpo = PING } = {}) {
  return new Promise((resolve, reject) => {
    const q = request({ host: '127.0.0.1', port: porta, method: metodo, path: caminho,
      headers: { 'content-type': 'application/json', accept: 'application/json, text/event-stream', ...headers } }, r => {
      let b = ''; r.on('data', d => b += d); r.on('end', () => resolve({ status: r.statusCode, corpo: b, headers: r.headers }));
    });
    q.on('error', reject);
    if (metodo === 'POST') q.end(corpo); else q.end();
  });
}
const bearer = t => ({ authorization: `Bearer ${t}` });

test('http: /saude responde sem token', async () => {
  const { s, porta } = await subir();
  const r = await pedir(porta, { metodo: 'GET', caminho: '/saude' });
  assert.equal(r.status, 200);
  s.close();
});

test('http: sem token, token errado ou vencido recebem 401 sem detalhe', async () => {
  const { s, porta } = await subir();
  for (const h of [{}, bearer('errado'), bearer(VENCIDO), { authorization: TOKEN }]) {
    const r = await pedir(porta, { headers: h });
    assert.equal(r.status, 401, JSON.stringify(h));
    assert.ok(!/vencid|expir|inexistente/i.test(r.corpo), r.corpo);
  }
  s.close();
});

test('http: Host fora da lista e Origin desconhecida sao recusados', async () => {
  const { s, porta } = await subir();
  assert.equal((await pedir(porta, { headers: { ...bearer(TOKEN), host: 'evil.example' } })).status, 403);
  assert.equal((await pedir(porta, { headers: { ...bearer(TOKEN), origin: 'https://evil.example' } })).status, 403);
  assert.equal((await pedir(porta, { headers: bearer(TOKEN) })).status, 200);
  s.close();
});

test('http: cliente MCP com token lista e chama ferramentas; auditoria sem a saida', async () => {
  const { s, porta, auditoria } = await subir();
  const t = new StreamableHTTPClientTransport(new URL(`http://127.0.0.1:${porta}/diag`), { requestInit: { headers: bearer(TOKEN) } });
  const c = new Client({ name: 'teste', version: '1' });
  await c.connect(t);
  assert.ok((await c.listTools()).tools.some(x => x.name === 'containers'));
  const r = await c.callTool({ name: 'containers', arguments: {} });
  assert.match(r.content[0].text, /ez-api\|Up 2 hours/);
  await c.close();
  const chamada = auditoria.find(l => l.ferramenta === 'containers');
  assert.ok(chamada, JSON.stringify(auditoria));
  assert.equal(chamada.pessoa, 'teste');
  assert.equal(typeof chamada.ms, 'number');
  assert.ok(!JSON.stringify(auditoria).includes('Up 2 hours'));
  assert.ok(!JSON.stringify(auditoria).includes(TOKEN));
  s.close();
});

test('http: revogacao vale sem reiniciar', async () => {
  const { s, porta, tokens } = await subir();
  assert.equal((await pedir(porta, { headers: bearer(TOKEN) })).status, 200);
  writeFileSync(tokens, '# vazio\n');
  assert.equal((await pedir(porta, { headers: bearer(TOKEN) })).status, 401);
  s.close();
});

test('http: limite por token devolve 429', async () => {
  const { s, porta } = await subir({ limitePorMinuto: 2 });
  assert.equal((await pedir(porta, { headers: bearer(TOKEN) })).status, 200);
  assert.equal((await pedir(porta, { headers: bearer(TOKEN) })).status, 200);
  assert.equal((await pedir(porta, { headers: bearer(TOKEN) })).status, 429);
  s.close();
});

test('http: IP com tokens errados demais fica bloqueado, mesmo com token valido', async () => {
  const { s, porta } = await subir({ falhasPorIp: 2 });
  const ip = { 'x-forwarded-for': '203.0.113.9' };
  assert.equal((await pedir(porta, { headers: { ...ip, ...bearer('x1') } })).status, 401);
  assert.equal((await pedir(porta, { headers: { ...ip, ...bearer('x2') } })).status, 401);
  assert.equal((await pedir(porta, { headers: { ...ip, ...bearer(TOKEN) } })).status, 429);
  assert.equal((await pedir(porta, { headers: { 'x-forwarded-for': '203.0.113.10', ...bearer(TOKEN) } })).status, 200);
  s.close();
});

test('http: corpo grande, metodo errado e caminho desconhecido', async () => {
  const { s, porta } = await subir();
  assert.equal((await pedir(porta, { headers: bearer(TOKEN), corpo: 'x'.repeat(70 * 1024) })).status, 413);
  assert.equal((await pedir(porta, { metodo: 'GET', headers: bearer(TOKEN) })).status, 405);
  assert.equal((await pedir(porta, { caminho: '/db', headers: bearer(TOKEN) })).status, 404);
  assert.equal((await pedir(porta, { headers: bearer(TOKEN), corpo: '{nao json' })).status, 400);
  s.close();
});
