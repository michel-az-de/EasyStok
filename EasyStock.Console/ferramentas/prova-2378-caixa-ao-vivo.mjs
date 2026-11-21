/* eslint-disable no-console */
// Prova da #2378: o caixa do dia acompanha o que acontece fora da aba Caixa.
//   1. Pix confirmado por webhook e "Recebi na porta" chegam como evento de pedido/pagamento: o caixa relê;
//   2. resposta de leitura antiga não sobrescreve a mais nova (geração);
//   3. a virada do dia no fuso da loja (America/Sao_Paulo) é percebida, e meia-noite UTC não engana;
//   4. o estorno repetido depois de resposta perdida leva a mesma Idempotency-Key; abrir e fechar não levam
//      (a API não as cobre: pendência registrada na issue);
//   5. o hook usa as regras acima (relê por SSE, foco/visibilidade e virada do dia, com geração).
//
//   node ferramentas/prova-2378-caixa-ao-vivo.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    return proximo(url, contexto)
  },
})

const { recarregaCaixaCom, criarGeracaoDeLeitura, criarVigiaDoDia } = await import('../src/aplicacao/caixaAoVivo.js')
const { criarAcoesCaixaApi } = await import('../src/aplicacao/api/caixa.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

// --- 1. O que faz o caixa reler -------------------------------------------------------
await confere('pedido pago, pagamento atualizado, pagamento capturado e ready releem', () => {
  for (const evento of ['ready', 'pedido.pago', 'pedido.pagamento_atualizado', 'pedido.estorno_atualizado', 'pagamento.capturado']) {
    assert.equal(recarregaCaixaCom(evento), true, evento)
  }
})
await confere('evento que não mexe em dinheiro não relê', () => {
  for (const evento of ['impressao.pendente', 'conversa.mensagem', 'estoque.baixo']) assert.equal(recarregaCaixaCom(evento), false, evento)
})

// --- 2. Resposta fora de ordem ---------------------------------------------------------
await confere('só a leitura mais nova vale', () => {
  const geracao = criarGeracaoDeLeitura()
  const antiga = geracao.nova()
  const nova = geracao.nova()
  assert.equal(geracao.vale(antiga), false)
  assert.equal(geracao.vale(nova), true)
})
await confere('desmontar a tela (geração nova) invalida a leitura em voo', () => {
  const geracao = criarGeracaoDeLeitura()
  const emVoo = geracao.nova()
  geracao.nova()
  assert.equal(geracao.vale(emVoo), false)
})

// --- 3. Virada do dia no fuso da loja ---------------------------------------------------
await confere('23:59 de Brasília para 00:01 de Brasília é virada', () => {
  let agora = Date.parse('2026-11-11T02:59:00Z') // 10/11 23:59 em Brasília
  const vigia = criarVigiaDoDia(() => agora)
  assert.equal(vigia.virou(), false)
  agora = Date.parse('2026-11-11T03:01:00Z') // 11/11 00:01 em Brasília
  assert.equal(vigia.virou(), true)
  assert.equal(vigia.virou(), false)
})
await confere('meia-noite UTC (21:00 em Brasília) não é virada', () => {
  let agora = Date.parse('2026-11-10T23:30:00Z')
  const vigia = criarVigiaDoDia(() => agora)
  agora = Date.parse('2026-11-11T00:30:00Z')
  assert.equal(vigia.virou(), false)
})

// --- 4. Idempotency-Key no estorno -------------------------------------------------------
function montarFetch(responder) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body, headers } = {}) => {
    const chamada = { metodo: method, url, corpo: body ? JSON.parse(body) : null, chave: new Headers(headers).get('Idempotency-Key') }
    chamadas.push(chamada)
    const { status = 200, data = null, cair = false } = responder(chamada, chamadas.length) ?? {}
    if (cair) throw new TypeError('Failed to fetch')
    return new Response(status === 204 ? null : JSON.stringify({ data }), { status })
  }
  return chamadas
}
await confere('estorno repetido depois de resposta perdida leva a mesma chave; gravou, chave nova', async () => {
  const chamadas = montarFetch((_c, n) => (n === 1 ? { cair: true } : { data: { id: 'm-1' } }))
  let serie = 0
  const acoes = criarAcoesCaixaApi({ gerarChave: () => `chave-${serie += 1}` })
  await assert.rejects(() => acoes.estornarMovimentoCaixa(0, 'm-1', 'Valor errado'))
  await acoes.estornarMovimentoCaixa(0, 'm-1', 'Valor errado')
  await acoes.estornarMovimentoCaixa(0, 'm-1', 'Valor errado')
  assert.deepEqual(chamadas.map((c) => c.chave), ['chave-1', 'chave-1', 'chave-2'])
  assert.ok(chamadas.every((c) => c.url.endsWith('/api/caixa/movimentos/m-1/estornar')))
})
await confere('abrir e fechar seguem sem chave (a API não as cobre)', async () => {
  const chamadas = montarFetch(() => ({ data: {} }))
  const acoes = criarAcoesCaixaApi({ gerarChave: () => 'chave-x' })
  await acoes.abrirCaixa(0, 100, '')
  await acoes.fecharCaixa(0, 100, 100, '')
  assert.deepEqual(chamadas.map((c) => c.chave), [null, null])
})

// --- 5. O hook usa as regras --------------------------------------------------------------
const hook = readFileSync(new URL('../src/aplicacao/useCaixaDoDiaApi.js', import.meta.url), 'utf8')
await confere('o hook relê por SSE, foco, visibilidade e virada do dia', () => {
  assert.match(hook, /ligarRecargaAoVivo\(\{ recarregar, recarregaCom: recarregaCaixaCom \}\)/)
  assert.match(hook, /addEventListener\('visibilitychange'/)
  assert.match(hook, /addEventListener\('focus'/)
  assert.match(hook, /vigia\.virou\(\)/)
})
await confere('toda leitura passa pela geração e a carga inicial usa recarregar', () => {
  assert.match(hook, /geracao\.nova\(\)/)
  assert.match(hook, /geracao\.vale\(minha\)/)
  assert.doesNotMatch(hook, /let vivo/, 'a carga inicial não pode ter caminho próprio sem geração')
})

console.log(`\n${passou} ok, ${falhas.length} falhas`)
if (falhas.length) process.exit(1)
