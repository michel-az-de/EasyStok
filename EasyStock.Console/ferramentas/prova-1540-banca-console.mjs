/* eslint-disable no-console */
// Prova da #1540 (banca do console, rodada de 10/10): a parte que roda sem React.
//   1. preço de porção digitado com ponto ("34.90") é trinta e quatro e noventa, não R$ 3.490;
//   2. receita aceita quantidade com vírgula ("1,2") e manda o número;
//   3. peso que não é número inteiro ("350g") trava a linha da produção em vez de ir nulo;
//   4. sangria repetida logo depois de resposta perdida usa a mesma Idempotency-Key; gravou ou
//      passou do prazo, chave nova;
//   5. "Desfazer o último" manda o id do pagamento que a tela mostrou;
//   6. perda de prato com porções pede a porção e manda a porção do estoque (M1.4c).
//
//   node ferramentas/prova-1540-banca-console.mjs

import { registerHooks } from 'node:module'
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

const { precoDoTexto, corpoDasPorcoes, erroDasPorcoes } = await import('../src/dominio/porcoes.js')
const { erroDaReceita, criarGestaoReceitas } = await import('../src/aplicacao/receitas.js')
const { erroDaLinha } = await import('../src/dominio/producao.js')

const { criarAcoesCaixaApi } = await import('../src/aplicacao/api/caixa.js')
const { criarChaveDeEnvio, VALIDADE_DA_CHAVE_MS } = await import('../src/aplicacao/chaveDeEnvio.js')
const { ultimoPagamentoApi } = await import('../src/dominio/pagamento.js')
const { criarGestaoPerdas, erroDaPerda, porcoesComSaldo } = await import('../src/aplicacao/perdas.js')

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

// Cada resposta pode ser um objeto ou uma função (para falhar só na primeira chamada).
function montarFetch(responder) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body, headers } = {}) => {
    const chamada = { metodo: method, url, corpo: body ? JSON.parse(body) : null, chave: new Headers(headers).get('Idempotency-Key') }
    chamadas.push(chamada)
    const { status = 200, data = null, error, cair = false } = responder(chamada, chamadas.length) ?? {}
    if (cair) throw new TypeError('Failed to fetch')
    return new Response(status === 204 ? null : JSON.stringify(error ? { error } : { data }), { status })
  }
  return chamadas
}

// --- 1. Preço da porção ----------------------------------------------------------------
await confere('"34.90" e "34,90" são trinta e quatro e noventa', () => {
  assert.equal(precoDoTexto('34.90'), 34.9)
  assert.equal(precoDoTexto('34,90'), 34.9)
  assert.equal(precoDoTexto('28.5'), 28.5)
})
await confere('milhar continua milhar e vazio continua sem preço', () => {
  assert.equal(precoDoTexto('1.250,00'), 1250)
  assert.equal(precoDoTexto('28,50'), 28.5)
  assert.ok(Number.isNaN(precoDoTexto('  ')))
  assert.equal(erroDasPorcoes([{ rotulo: '300 g', preco: '' }]), 'Cada porção precisa de preço.')
})
await confere('o corpo enviado leva o preço digitado com ponto sem multiplicar por cem', () => {
  const [porcao] = corpoDasPorcoes([{ id: null, rotulo: '300 g', preco: '34.90', peso: '', disponivel: true, padrao: true, sku: null }])
  assert.equal(porcao.preco, 34.9)
})

// --- 2. Receita com vírgula -----------------------------------------------------------
await confere('quantidade "1,2" e rendimento "2,5" passam na validação', () => {
  assert.equal(erroDaReceita({ rendimento: '2,5', linhas: [{ insumoId: 's-1', quantidade: '1,2' }] }), null)
  assert.equal(erroDaReceita({ rendimento: '6', linhas: [{ insumoId: 's-1', quantidade: '' }] }), 'Quantidade de cada insumo maior que zero.')
})
await confere('salvar manda 1.2, não NaN', async () => {
  const chamadas = montarFetch(() => ({ status: 204 }))
  const erros = []
  const gestao = criarGestaoReceitas({ recarregar: async () => {}, aoErro: (m) => erros.push(m) })
  const ok = await gestao.salvar('p-1', {
    rendimento: '6', unidadeRendimento: 'Un', unidadeMedidaBase: 'Un', linhas: [{ insumoId: 's-1', quantidade: '1,2', unidade: 'Kg' }],
  })
  assert.deepEqual(erros, [])
  assert.equal(ok, true)
  assert.equal(chamadas.find((c) => c.metodo === 'PUT').corpo.linhas[0].quantidade, 1.2)
})

// --- 3. Peso da produção ---------------------------------------------------------------
const linha = { sku: 'i-1', porcoes: 2, validadeDias: 5, pesoPorPorcaoG: null, pesoRealG: null }
await confere('peso que não é número trava a linha', () => {
  assert.equal(erroDaLinha({ ...linha, pesoPorPorcaoG: Number('350g') }), 'Peso da porção em gramas inteiros.')
  assert.equal(erroDaLinha({ ...linha, pesoRealG: Number('1144,5') }), 'Peso real em gramas inteiros.')
  assert.equal(erroDaLinha({ ...linha, pesoPorPorcaoG: 350.5 }), 'Peso da porção em gramas inteiros.')
})
await confere('sem peso ou com gramas inteiros a linha segue', () => {
  assert.equal(erroDaLinha(linha), null)
  assert.equal(erroDaLinha({ ...linha, pesoPorPorcaoG: 500, pesoRealG: 1144 }), null)
})

// --- 4. Chave do lançamento de caixa -------------------------------------------------
const sangria = { tipoMovimento: 'saida', categoria: 'Sangria', valor: 300, meio: 'dinheiro', descricao: 'Depósito' }
await confere('resposta perdida: a segunda tentativa usa a mesma chave', async () => {
  const chamadas = montarFetch((_c, n) => (n === 1 ? { cair: true } : { data: { id: 'm-1' } }))
  let serie = 0
  const acoes = criarAcoesCaixaApi({ gerarChave: () => `chave-${serie += 1}` })
  await assert.rejects(() => acoes.lancarMovimentoCaixa(0, sangria))
  await acoes.lancarMovimentoCaixa(0, sangria)
  assert.deepEqual(chamadas.map((c) => c.chave), ['chave-1', 'chave-1'])
  assert.ok(chamadas.every((c) => c.url.endsWith('/api/caixa/movimentos')))
})
await confere('gravou: a próxima sangria igual é outro lançamento, com chave nova', async () => {
  const chamadas = montarFetch(() => ({ data: { id: 'm-1' } }))
  let serie = 0
  const acoes = criarAcoesCaixaApi({ gerarChave: () => `chave-${serie += 1}` })
  await acoes.lancarMovimentoCaixa(0, sangria)
  await acoes.lancarMovimentoCaixa(0, sangria)
  assert.deepEqual(chamadas.map((c) => c.chave), ['chave-1', 'chave-2'])
})
await confere('valor corrigido depois da falha ganha chave nova', () => {
  let serie = 0
  const envio = criarChaveDeEnvio(() => `chave-${serie += 1}`)
  const primeira = envio.para({ valor: 300 })
  assert.equal(envio.para({ valor: 300 }), primeira)
  assert.notEqual(envio.para({ valor: 30 }), primeira)
})
await confere('a chave vence: sangria igual horas depois de resposta perdida é outro lançamento', () => {
  let serie = 0
  let relogio = 0
  const envio = criarChaveDeEnvio(() => `chave-${serie += 1}`, () => relogio)
  const primeira = envio.para({ valor: 200 })
  relogio = VALIDADE_DA_CHAVE_MS
  assert.equal(envio.para({ valor: 200 }), primeira, 'dentro do prazo é a retentativa')
  relogio = 2 * VALIDADE_DA_CHAVE_MS + 1
  assert.notEqual(envio.para({ valor: 200 }), primeira, 'fora do prazo não reaproveita a resposta guardada')
})

// --- 5. Último pagamento -----------------------------------------------------------------
await confere('o último pagamento é o mais recente, e leva o id que a API espera', () => {
  const pedido = { pagamentosApi: [{ valor: 50, em: 1000, pagamentoId: 'a' }, { valor: 30, em: 2000, pagamentoId: 'b' }] }
  assert.deepEqual(ultimoPagamentoApi(pedido), { valor: 30, em: 2000, pagamentoId: 'b' })
  assert.equal(ultimoPagamentoApi({ pagamentosApi: [] }), null)
  assert.equal(ultimoPagamentoApi(null), null)
})

// --- 6. Perda por porção ----------------------------------------------------------------
const ravioli = {
  porcoes: [
    { variacaoId: 'v-300', rotulo: '300 g', saldo: 4, produtoVariacaoId: 'pv-300' },
    { variacaoId: 'v-800', rotulo: '800 g', saldo: 0, produtoVariacaoId: 'pv-800' },
    { variacaoId: null, rotulo: 'sem porção', saldo: 2, produtoVariacaoId: null },
  ],
}
const perda = { produtoId: 'p-1', quantidade: '1', motivo: 'PerdaNoPreparo', texto: '' }
await confere('só porção com saldo vira opção, e a "sem porção" tem chave própria', () => {
  assert.deepEqual(porcoesComSaldo(ravioli).map((s) => [s.chave, s.produtoVariacaoId]), [['v-300', 'pv-300'], ['sem-porcao', null]])
  assert.deepEqual(porcoesComSaldo({ porcoes: [] }), [])
})
await confere('prato com porções exige a porção; insumo e prato sem porção seguem como antes', () => {
  const porcoes = porcoesComSaldo(ravioli)
  assert.equal(erroDaPerda({ ...perda, porcoes, porcao: '' }), 'Escolha a porção perdida.')
  assert.equal(erroDaPerda({ ...perda, porcoes, porcao: 'v-300' }), null)
  assert.equal(erroDaPerda(perda), null)
})
await confere('a perda manda a porção do estoque escolhida; "sem porção" não manda porção', async () => {
  const chamadas = montarFetch(() => ({ data: { quantidade: 1 } }))
  const gestao = criarGestaoPerdas({ recarregar: async () => {}, aoErro: () => {} })
  const porcoes = porcoesComSaldo(ravioli)
  assert.equal(await gestao.lancar({ ...perda, porcoes, porcao: 'v-300' }), true)
  assert.equal(await gestao.lancar({ ...perda, porcoes, porcao: 'sem-porcao' }), true)
  const enviados = chamadas.filter((c) => c.metodo === 'POST').map((c) => c.corpo.produtoVariacaoId ?? null)
  assert.deepEqual(enviados, ['pv-300', null])
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length) process.exit(1)
