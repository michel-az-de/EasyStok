import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { criarCliente, validarBase } from '../src/lib/api.mjs';
import { consultas, escritas, escrever } from '../src/lib/caixa.mjs';

const subir = handler => new Promise(r => { const s = createServer(handler); s.listen(0, '127.0.0.1', () => r({ s, base: `http://127.0.0.1:${s.address().port}` })); });
const env = { BABA_API_EMAIL: 'a@b.c', BABA_API_PASSWORD: 'x' };

test('base: so https ou http local', () => {
  assert.ok(validarBase('https://api.easystok.online'));
  assert.ok(validarBase('http://127.0.0.1:5000'));
  for (const b of ['http://api.easystok.online', 'ftp://x', 'https://x/../y', 'http://evil.com'])
    assert.throws(() => validarBase(b), b);
});

test('cliente: login, bearer, relogin no 401, caminho fora da lista recusado', async () => {
  let logins = 0, fase = 0; const vistos = [];
  const { s, base } = await subir((q, r) => {
    let b = ''; q.on('data', d => b += d); q.on('end', () => {
      vistos.push(`${q.method} ${q.url} ${q.headers.authorization ?? ''}`);
      r.setHeader('content-type', 'application/json');
      if (q.url === '/api/auth/login') { logins++; assert.deepEqual(JSON.parse(b), { email: 'a@b.c', senha: 'x' }); return r.end(JSON.stringify({ data: { token: 't' + logins, usuario: { nome: 'F', nivel: 'Admin' } } })); }
      if (fase++ === 0) { r.statusCode = 401; return r.end('{}'); }
      r.end(JSON.stringify({ data: { ok: 1 } }));
    });
  });
  try {
    const c = criarCliente({ base, env });
    assert.deepEqual((await c.get('/api/caixa/dia')).data, { ok: 1 });
    assert.equal(logins, 2);
    assert.match(vistos.at(-1), /Bearer t2/);
    await assert.rejects(() => c.get('/api/usuarios'), /nao permitido/);
    await assert.rejects(() => c.get('/api/caixa/../auth'), /nao permitido/);
  } finally { s.close(); }
});

test('cliente: sem credenciais falha sem chamar a API', async () => {
  await assert.rejects(() => criarCliente({ base: 'http://127.0.0.1:1', env: {} }).get('/api/caixa/dia'), /BABA_API_EMAIL/);
});

test('consultas: monta query e recusa data/uuid invalidos', () => {
  assert.equal(consultas.dia({ data: '2026-11-14' }), '/api/caixa/dia?data=2026-11-14');
  assert.equal(consultas.movimentos({ tipo: 'saida', por_pagina: 500 }), '/api/caixa/movimentos?tipo=saida&page=1&pageSize=100');
  assert.throws(() => consultas.dia({ data: '14/11/2026' }));
  assert.throws(() => consultas.dia({ data: '2026-11-14&x=1' }));
  assert.throws(() => consultas.dia({ loja_id: 'x' }));
});

test('escritas: validacao do movimento', () => {
  assert.throws(() => escritas.movimento({ tipo: 'x', valor: 1 }));
  assert.throws(() => escritas.movimento({ tipo: 'entrada', valor: 0 }));
  assert.throws(() => escritas.movimento({ tipo: 'entrada', valor: -5 }));
  assert.match(escritas.movimento({ tipo: 'saida', valor: 10, descricao: 'troco' }).resumo, /SAIDA de R\$ 10\.00/);
});

test('escrever: desligada por padrao, exige confirmacao e nao posta se negada', async () => {
  const chamadas = []; const cliente = { post: async (c, b) => { chamadas.push([c, b]); return { data: 1 }; } };
  const plano = escritas.abrir({ saldo_inicial: 50 });
  await assert.rejects(() => escrever({ env: {}, cliente, pedir: async () => true, plano }), /desligada/);
  assert.equal(chamadas.length, 0);
  assert.deepEqual(await escrever({ env: { BABA_CAIXA_ESCRITA: '1' }, cliente, pedir: async () => false, plano }), { feito: false, motivo: 'nao confirmado pela pessoa' });
  assert.equal(chamadas.length, 0);
  const r = await escrever({ env: { BABA_CAIXA_ESCRITA: '1' }, cliente, pedir: async m => { assert.match(m, /ABRIR caixa/); return true; }, plano });
  assert.equal(r.feito, true);
  assert.deepEqual(chamadas, [['/api/caixa/abrir', { saldoInicial: 50 }]]);
});
