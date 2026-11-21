import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { InMemoryTransport } from '@modelcontextprotocol/sdk/inMemory.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';
import { criarServidorDiag } from '../src/diag/fabrica.mjs';

const FERRAMENTAS = ['containers', 'deploy_historico', 'disco_e_fila', 'erros_recentes', 'logs_api', 'logs_worker', 'saude', 'sobre'];

async function conectar(opcoes) {
  const [ct, st] = InMemoryTransport.createLinkedPair();
  await criarServidorDiag(opcoes).connect(st);
  const c = new Client({ name: 'teste', version: '1' });
  await c.connect(ct);
  return c;
}

test('fabrica: registra as ferramentas do diag', async () => {
  const c = await conectar({ executar: async () => '' });
  assert.deepEqual((await c.listTools()).tools.map(t => t.name).sort(), FERRAMENTAS);
  await c.close();
});

test('fabrica: ferramentas remotas usam o executor injetado e redigem', async () => {
  const pedidos = [];
  const c = await conectar({ executar: async cmd => { pedidos.push(cmd); return 'ez-api|Up 2 hours|img maria@example.com'; } });
  const r = await c.callTool({ name: 'containers', arguments: {} });
  assert.match(r.content[0].text, /ez-api\|Up 2 hours/);
  assert.ok(!r.content[0].text.includes('maria@example.com'));
  await c.callTool({ name: 'logs_api', arguments: { since: '2h', tail: 10 } });
  assert.deepEqual(pedidos, ['containers', 'logs ez-api 2h 10']);
  await c.close();
});

test('fabrica: sobre usa GIT_SHA do ambiente (imagem sem .git)', async () => {
  const antes = process.env.GIT_SHA;
  process.env.GIT_SHA = 'abc1234';
  try {
    const c = await conectar({ executar: async () => '' });
    assert.match((await c.callTool({ name: 'sobre', arguments: {} })).content[0].text, /commit=abc1234/);
    await c.close();
  } finally { if (antes === undefined) delete process.env.GIT_SHA; else process.env.GIT_SHA = antes; }
});

test('stdio: a entrada local continua listando as ferramentas', async () => {
  const t = new StdioClientTransport({ command: process.execPath, args: [fileURLToPath(new URL('../src/diag/server.mjs', import.meta.url))] });
  const c = new Client({ name: 'teste', version: '1' });
  await c.connect(t);
  assert.deepEqual((await c.listTools()).tools.map(x => x.name).sort(), FERRAMENTAS);
  await c.close();
});
