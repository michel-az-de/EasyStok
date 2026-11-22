// #3076 (E4, L1): a baixa à mão é uma função só, usada pela conversa e pelo detalhe de Pedidos. Prova as regras
// que a spec exige dela: a mesma chave de idempotência no reenvio, a recusa sem aprovação, o link do Mercado Pago
// cancelado antes da baixa, a mensagem da API para `pedido_ja_pago` e o aviso quando a rede não confirmou.
// E que a conversa e o detalhe chamam a mesma função (sem um segundo caminho de `POST /pagamentos`).
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      try { return proximo(spec + '.js', contexto) } catch { /* resolução normal */ }
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})
globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
const { criarRegistroDePagamento, SEM_CONFIRMACAO_DO_PAGAMENTO } = await import('../src/aplicacao/pagamentoDoPedido.js')
const { MOTIVO_BAIXA_SEM_APROVACAO } = await import('../src/dominio/cobranca.js')

const pedidoId = 'p1'
const operacional = { pedidoId, statusApi: 'pronto' }
function montar(respostas) {
  const chamadas = []
  let n = 0
  const proxima = () => { const r = respostas[Math.min(n, respostas.length - 1)]; n += 1; return r }
  const registrar = (id, valor, metodo, chave) => { chamadas.push({ tipo: 'pagamento', id, valor, metodo, chave }); const r = proxima(); return r instanceof Error ? Promise.reject(r) : Promise.resolve(r) }
  const trocarForma = (id, forma) => { chamadas.push({ tipo: 'forma', id, forma }); return Promise.resolve({}) }
  return { chamadas, registrar: criarRegistroDePagamento({ registrar, trocarForma }) }
}
const erro = (mensagem, extra = {}) => Object.assign(new Error(mensagem), extra)
let testes = 0

// 1. Reenvio depois de queda: mesma chave; depois do sucesso, chave nova.
{
  const t = montar([erro('Failed to fetch'), {}, {}])
  const a = await t.registrar({ pedidoId, pedido: operacional, valor: 50, metodo: 'pix' })
  assert.equal(a.erro, SEM_CONFIRMACAO_DO_PAGAMENTO); assert.equal(a.semConfirmacao, true)
  assert.deepEqual(await t.registrar({ pedidoId, pedido: operacional, valor: 50, metodo: 'pix' }), { ok: true })
  await t.registrar({ pedidoId, pedido: operacional, valor: 50, metodo: 'pix' })
  const [c1, c2, c3] = t.chamadas
  assert.equal(c1.chave, c2.chave, 'reenvio após queda usa a mesma chave')
  assert.notEqual(c2.chave, c3.chave, 'envio confirmado libera chave nova'); testes++
}
// 2. Sem aprovação: nada sai.
{
  const t = montar([{}])
  const r = await t.registrar({ pedidoId, pedido: { pedidoId, statusApi: 'aguardando_aprovacao_baba' }, valor: 50, metodo: 'pix' })
  assert.equal(r.erro, MOTIVO_BAIXA_SEM_APROVACAO); assert.equal(t.chamadas.length, 0); testes++
}
// 3. Link pendente: a forma vira "na entrega" antes da baixa.
{
  const t = montar([{}])
  const comLink = { pedidoId, statusApi: 'aguardando_pagamento', cobranca: { link: 'https://example.test/pagar' } }
  assert.deepEqual(await t.registrar({ pedidoId, pedido: comLink, valor: 80, metodo: 'dinheiro' }), { ok: true })
  assert.deepEqual(t.chamadas.map((c) => c.tipo + (c.forma ? ':' + c.forma : '')), ['forma:na_entrega', 'pagamento']); testes++
}
// 4. `pedido_ja_pago`: a mensagem da API, não o erro genérico.
{
  const t = montar([erro('Este pedido já está pago.', { status: 409, codigo: 'pedido_ja_pago' })])
  const r = await t.registrar({ pedidoId, pedido: operacional, valor: 50, metodo: 'pix' })
  assert.equal(r.erro, 'Este pedido já está pago.'); assert.equal(r.semConfirmacao, false); testes++
}
// 5. Recusa de regra (excede o pendente): mensagem da API com o prefixo de sempre.
{
  const t = montar([erro('Valor do pagamento (R$ 100,00) excede o pendente (R$ 80,00).', { status: 400 })])
  const r = await t.registrar({ pedidoId, pedido: operacional, valor: 100, metodo: 'pix' })
  assert.equal(r.erro, 'Pagamento não registrado: Valor do pagamento (R$ 100,00) excede o pendente (R$ 80,00).'); testes++
}
// 6. Um caminho só: a conversa e o detalhe usam a função; nenhum dos dois chama o POST de pagamento direto.
{
  const raiz = new URL('../src/', import.meta.url)
  const ler = (caminho) => readFileSync(new URL(caminho, raiz), 'utf8')
  for (const arquivo of ['aplicacao/api/comanda.js', 'aplicacao/pedidosCadastro.js']) {
    const fonte = ler(arquivo)
    assert.match(fonte, /criarRegistroDePagamento\(/, `${arquivo} usa a função compartilhada`)
    assert.doesNotMatch(fonte, /registrarPagamentoManual\(/, `${arquivo} não chama o POST de pagamento por fora`)
  }
  testes++
}
console.log(`prova-3076-pagamento-do-pedido: ${testes} cenários ok`)
