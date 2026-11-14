import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { criarCliente } from '../src/lib/api.mjs';
import { consultas } from '../src/lib/atendimento.mjs';

const U = '11111111-1111-1111-1111-111111111111';
const env = { BABA_API_EMAIL: 'a@b.c', BABA_API_PASSWORD: 'x' };

test('consultas: caminhos e validacao', () => {
  assert.equal(consultas.conversas({ situacao: 'Aberta', limite: 20 }), '/api/atendimento/conversas?situacao=Aberta&limite=20');
  assert.equal(consultas.conversas({ responsavel: 'eu', q: 'joao silva' }), '/api/atendimento/conversas?q=joao%20silva&responsavel=eu');
  assert.equal(consultas.mensagens({ conversa_id: U, antes_de: '2026-11-14T10:00:00Z' }), `/api/atendimento/conversas/${U}/mensagens?antesDe=2026-11-14T10%3A00%3A00Z`);
  assert.equal(consultas.nao_entregues({}), '/api/atendimento/conversas/nao-entregues');
  assert.equal(consultas.agenda({ de: '2026-11-14', ate: '2026-11-15' }), '/api/atendimento/agenda?de=2026-11-14&ate=2026-11-15');
  assert.throws(() => consultas.conversas({ situacao: 'a&b=1' }));
  assert.throws(() => consultas.conversas({ responsavel: 'todos' }), /UUID/);
  assert.throws(() => consultas.conversas({ limite: 1000 }), /limite/);
  assert.throws(() => consultas.mensagens({}), /obrigatorio/);
  assert.throws(() => consultas.mensagens({ conversa_id: U, antes_de: 'ontem' }), /antes_de/);
  assert.throws(() => consultas.agenda({ de: '2026-11-14' }), /obrigatoria/);
  assert.throws(() => consultas.assistente_uso({ mes: 13 }), /mes/);
});

test('cliente: GET liberado nas rotas de leitura; escrita e dados sensiveis recusados', async () => {
  const vistos = [];
  const s = createServer((q, r) => { vistos.push(`${q.method} ${q.url}`); r.setHeader('content-type', 'application/json'); r.end(JSON.stringify({ data: { token: 't', usuario: {} } })); });
  await new Promise(r => s.listen(0, '127.0.0.1', r));
  try {
    const c = criarCliente({ base: `http://127.0.0.1:${s.address().port}`, env });
    for (const f of [consultas.conversas({}), consultas.mensagens({ conversa_id: U }), consultas.assistente_turno({ conversa_id: U }), consultas.viagem({ viagem_id: U }), consultas.expediente(), consultas.assistente_historico({}), consultas.assistente_uso({})])
      await c.get(f);
    for (const p of [`/api/atendimento/conversas/${U}/dossie`, `/api/atendimento/conversas/${U}/mensagens/${U}/midia`, `/api/atendimento/entregadores`, '/api/atendimento/avisos/equipe', '/api/atendimento/conversas/../x'])
      await assert.rejects(() => c.get(p), /nao permitido/, p);
    for (const p of [`/api/atendimento/conversas/${U}/mensagens`, `/api/atendimento/conversas/${U}/encerrar`, '/api/atendimento/producao/receitas/x/ligar', '/api/atendimento/assistente', '/api/atendimento/producao/insumos'])
      await assert.rejects(() => c.post(p, {}), /nao permitido/, p);
  } finally { s.close(); }
  assert.ok(vistos.every(v => !v.startsWith('POST') || v.includes('/auth/login')));
});
