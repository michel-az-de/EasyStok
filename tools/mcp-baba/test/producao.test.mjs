import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { criarCliente } from '../src/lib/api.mjs';
import { escrever } from '../src/lib/caixa.mjs';
import { consultas, escritas } from '../src/lib/producao.mjs';

const P = '11111111-1111-1111-1111-111111111111';
const env = { BABA_API_EMAIL: 'a@b.c', BABA_API_PASSWORD: 'x' };

test('consultas: caminhos e validacao', () => {
  assert.equal(consultas.estoque_do_dia(), '/api/atendimento/producao/estoque-do-dia');
  assert.equal(consultas.sugestao({ ate: '2026-11-15' }), '/api/atendimento/producao/sugestao?ate=2026-11-15');
  assert.equal(consultas.perdas({}), '/api/atendimento/producao/perdas');
  assert.equal(consultas.receita({ produto_id: P }), `/api/atendimento/producao/receitas/${P}`);
  assert.throws(() => consultas.receita({}), /obrigatorio/);
  assert.throws(() => consultas.receita({ produto_id: '../x' }), /UUID/);
  assert.throws(() => consultas.perdas({ de: '14/11/2026' }));
  assert.throws(() => consultas.sugestao({ ate: '2026-11-15&x=1' }));
});

test('cliente: aceita rotas de producao e recusa o resto do atendimento', async () => {
  const vistos = [];
  const s = createServer((q, r) => { vistos.push(q.url); r.setHeader('content-type', 'application/json'); r.end(JSON.stringify({ data: { token: 't', usuario: {} } })); });
  await new Promise(r => s.listen(0, '127.0.0.1', r));
  try {
    const c = criarCliente({ base: `http://127.0.0.1:${s.address().port}`, env });
    await c.get(consultas.receita({ produto_id: P }));
    await c.get(consultas.perdas({ de: '2026-11-01' }));
    assert.ok(vistos.includes(`/api/atendimento/producao/receitas/${P}`));
    for (const p of ['/api/atendimento/pedidos', '/api/atendimento/producao/../auth', '/api/atendimento/producao/x.y'])
      await assert.rejects(() => c.get(p), /nao permitido/, p);
  } finally { s.close(); }
});

test('perda: valida e mostra todos os campos no resumo', () => {
  const r = escritas.perda({ produto_id: P, quantidade: 2.5, motivo: 'Vencido', texto: 'x\ny', item_estoque_id: P }).resumo;
  assert.ok(!r.includes('\n'));
  for (const t of [/quantidade="2\.5"/, /motivo="Vencido"/, /item_estoque_id=/, /texto="x y"/]) assert.match(r, t);
  assert.throws(() => escritas.perda({ produto_id: P, quantidade: 0, motivo: 'Vencido' }), /quantidade/);
  assert.throws(() => escritas.perda({ produto_id: P, quantidade: 1.0001, motivo: 'Vencido' }), /quantidade/);
  assert.throws(() => escritas.perda({ produto_id: P, quantidade: 1, motivo: 'Roubo' }), /motivo/);
  assert.throws(() => escritas.perda({ quantidade: 1, motivo: 'Outro' }), /produto_id/);
  assert.throws(() => escritas.perda({ produto_id: P, quantidade: 1, motivo: 'Outro' }), /exige texto/);
});

test('escrever com outra flag: desligada, nao confirmada e confirmada', async () => {
  const chamadas = []; const cliente = { post: async (c, b) => { chamadas.push([c, b]); return { data: 1 }; } };
  const plano = escritas.perda({ produto_id: P, quantidade: 1, motivo: 'Doacao' });
  const flag = 'BABA_PRODUCAO_ESCRITA';
  await assert.rejects(() => escrever({ env: { BABA_CAIXA_ESCRITA: '1' }, cliente, pedir: async () => true, plano, flag }), /BABA_PRODUCAO_ESCRITA/);
  assert.equal(chamadas.length, 0);
  assert.equal((await escrever({ env: { [flag]: '1' }, cliente, pedir: async () => false, plano, flag })).feito, false);
  assert.equal(chamadas.length, 0);
  assert.equal((await escrever({ env: { [flag]: '1' }, cliente, pedir: async m => { assert.match(m, /LANCAR PERDA/); return true; }, plano, flag })).feito, true);
  assert.deepEqual(chamadas, [['/api/atendimento/producao/perdas', { produtoId: P, quantidade: 1, motivo: 'Doacao' }]]);
});
