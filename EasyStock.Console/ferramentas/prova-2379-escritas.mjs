// Prova da #2379: escritas do console que se perdiam ou ficavam sem resposta.
//
// 1. "Recebi R$ X" sai com keepalive (o envio pendente é descarregado ao esconder a aba).
// 2. Escrita sem keepalive tem prazo de 30 s e devolve o erro "sem resposta" (status 0).
// 3. Cancelar, desfazer e receber pagamento: sem resposta ou 5xx relê o pedido e avisa que confere.
// 4. Entregas descarta a leitura de uma geração velha.
// 5. `ocupado` das Entregas é um contador em voo: a primeira ação a terminar não solta a outra.
// 6. O efeito do SSE das comandas não depende de `versao` (não fecha e reabre a cada ação).
//
// Roda com: node ferramentas/prova-2379-escritas.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* resolução padrão */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})

globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok    ' + descricao) } catch (erro) {
    falhas.push(descricao)
    const motivo = String(erro.message).split('\n').filter(Boolean).slice(0, 4).join(' | ')
    console.log('FALHA ' + descricao + '\n      ' + motivo)
  }
}
// Módulo que ainda não existe no master reprova só o teste que o usa, pelo motivo certo.
const importar = (caminho) => import(caminho).catch((e) => ({ erroDeImportacao: e }))
const exige = (modulo, nome) => {
  if (modulo.erroDeImportacao) throw new Error(`módulo ausente: ${modulo.erroDeImportacao.message}`)
  return modulo[nome]
}

const prazo = await importar('../src/infra/api/prazo.js')
const { chamarApi } = await import('../src/infra/api/cliente.js')
const { registrarPagamentoManual } = await import('../src/infra/api/pagamentosPedidoApi.js')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
const geracaoMod = await importar('../src/aplicacao/geracaoDeLeitura.js')
const contadorMod = await importar('../src/aplicacao/contadorEmVoo.js')

// 1. keepalive no "Recebi"
await confere('registrarPagamentoManual sai com keepalive', async () => {
  let opcoes
  globalThis.fetch = async (_url, o) => { opcoes = o; return new Response(JSON.stringify({ data: {} })) }
  await registrarPagamentoManual('p1', 10, 'pix', 'chave')
  assert.equal(opcoes.keepalive, true)
  assert.equal(opcoes.headers['Idempotency-Key'], 'chave')
})

// 2. prazo por método
await confere('prazo: leitura 20 s, escrita 30 s, sem prazo para keepalive, arquivo, upload e sinal próprio', () => {
  const prazoDaChamada = exige(prazo, 'prazoDaChamada')
  assert.equal(prazoDaChamada({ metodo: 'GET' }), 20000)
  for (const metodo of ['POST', 'PUT', 'PATCH', 'DELETE']) assert.equal(prazoDaChamada({ metodo }), 30000, metodo)
  assert.equal(prazoDaChamada({ metodo: 'POST', keepalive: true }), null)
  assert.equal(prazoDaChamada({ metodo: 'GET', arquivo: true }), null)
  assert.equal(prazoDaChamada({ metodo: 'POST', formulario: {} }), null)
  assert.equal(prazoDaChamada({ metodo: 'POST', sinal: new AbortController().signal }), null)
  assert.equal(prazoDaChamada({ metodo: 'POST', sinal: new AbortController().signal, prazoComCancelamento: true }), 30000)
})

// #2401: rotas que chamam o modelo de linguagem passam de 30 s e eram abortadas.
await confere('prazo: chamada com prazoMs usa o prazo próprio e as rotas de IA do acervo pedem mais que 30 s', async () => {
  const prazoDaChamada = exige(prazo, 'prazoDaChamada')
  assert.equal(prazoDaChamada({ metodo: 'POST', prazoMs: 120000 }), 120000)
  assert.equal(prazoDaChamada({ metodo: 'POST' }), 30000)
  const { sugerirAssociacoes, dicaDoPrato } = await import('../src/infra/api/acervoApi.js')
  for (const chamar of [() => sugerirAssociacoes(), () => dicaDoPrato('i1')]) {
    const pedidos = []
    const original = AbortSignal.timeout
    AbortSignal.timeout = (ms) => { pedidos.push(ms); return original.call(AbortSignal, ms) }
    globalThis.fetch = async () => new Response(JSON.stringify({ data: {} }))
    try { await chamar() } finally { AbortSignal.timeout = original }
    assert.ok(pedidos.length === 1 && pedidos[0] > 30000, `prazo ${pedidos}`)
  }
})

await confere('escrita sem resposta no prazo vira erro status 0 com a frase da casa', async () => {
  const original = AbortSignal.timeout
  const pedidos = []
  AbortSignal.timeout = (ms) => { pedidos.push(ms); return original.call(AbortSignal, 5) }
  globalThis.fetch = (_url, o) => new Promise((_, rejeitar) => {
    if (!o.signal) return
    o.signal.addEventListener('abort', () => rejeitar(new DOMException('abortado', 'TimeoutError')))
  })
  try {
    const corrida = Promise.race([
      chamarApi('/api/x', { metodo: 'POST', corpo: {} }).then(() => null, (e) => e),
      new Promise((resolver) => setTimeout(() => resolver('pendurada'), 300)),
    ])
    const erro = await corrida
    assert.notEqual(erro, 'pendurada', 'a escrita ficou pendurada: sem prazo')
    assert.equal(erro.status, 0)
    assert.equal(erro.codigo, 'SEM_RESPOSTA')
    assert.equal(erro.message, 'Não houve resposta. Confira antes de repetir.')
    assert.deepEqual(pedidos, [30000])
  } finally { AbortSignal.timeout = original }
})

// 3. releitura depois de erro sem confirmação
const agora = '2026-10-06T15:00:00Z'
const dados = {
  pedidoId: '11111111-1111-1111-1111-111111111111', status: 'aguardando', total: 25, frete: 5,
  itens: [], totalPago: 25, pagamentos: [{ id: 'pagamento', valor: 25, metodo: 'dinheiro', pagoEm: agora }],
  cobranca: { cobrancaId: 'cobranca', provedor: 'na_entrega', status: 'Paga', valor: 25, valorPago: 25, criadaEm: agora, pagaEm: agora, metodoPagamento: 'dinheiro' },
}
const pedido = pedidoDaApi(dados)
function cenario(falhaDoPost) {
  const chamadas = []
  const despachados = []
  globalThis.fetch = async (url, o = {}) => {
    chamadas.push({ url, metodo: o.method ?? 'GET' })
    if ((o.method ?? 'GET') !== 'GET') return falhaDoPost()
    return new Response(JSON.stringify({ data: dados }))
  }
  const acoes = criarAcoesComandaApi({}, { despachar: (a) => despachados.push(a), estadoRef: { current: { conversas: [{ id: 'conversa', pedido }] } } })
  return { acoes, chamadas, despachados }
}
const semConexao = () => { throw new TypeError('rede caiu') }
const erro500 = () => new Response(JSON.stringify({ error: { message: 'Falha ao gravar' } }), { status: 500 })
const erro422 = () => new Response(JSON.stringify({ error: { message: 'Motivo inválido' } }), { status: 422 })
const leituras = (c) => c.chamadas.filter((x) => x.metodo === 'GET').length
const avisouConferencia = (c) => c.despachados.some((a) => /conferindo/i.test(a.mensagem ?? ''))

for (const [nome, falha] of [['sem conexão', semConexao], ['5xx', erro500]]) {
  await confere(`cancelar pedido (${nome}) relê o pedido e avisa que confere`, async () => {
    const c = cenario(falha)
    const r = await c.acoes.cancelarPedido('conversa', 'Cliente desistiu')
    assert.ok(r.erro)
    assert.ok(leituras(c) >= 1, 'não releu o pedido')
    assert.ok(avisouConferencia(c), 'sem aviso de conferência')
  })
  await confere(`desfazer pagamento (${nome}) relê o pedido e avisa que confere`, async () => {
    const c = cenario(falha)
    const r = await c.acoes.desfazerPagamento('conversa', 'Lançamento incorreto')
    assert.ok(r.erro)
    assert.ok(leituras(c) >= 1, 'não releu o pedido')
    assert.ok(avisouConferencia(c), 'sem aviso de conferência')
  })
  await confere(`receber pagamento (${nome}) relê o pedido e avisa que confere`, async () => {
    const c = cenario(falha)
    const r = await c.acoes.confirmarPagamento('conversa', 25, 'dinheiro')
    assert.ok(r.erro)
    assert.ok(leituras(c) >= 1, 'não releu o pedido')
    assert.ok(avisouConferencia(c), 'sem aviso de conferência')
  })
}
await confere('recusa da API (4xx) não relê: a resposta já diz o que houve', async () => {
  const c = cenario(erro422)
  await c.acoes.cancelarPedido('conversa', 'Cliente desistiu')
  await c.acoes.desfazerPagamento('conversa', 'Lançamento incorreto')
  assert.equal(leituras(c), 0)
})

// 4. geração da leitura
await confere('geração: só a leitura mais nova vale', () => {
  const g = exige(geracaoMod, 'criarGeracao')()
  const velha = g.nova()
  const nova = g.nova()
  assert.equal(g.vale(velha), false)
  assert.equal(g.vale(nova), true)
})
const fonte = (caminho) => readFileSync(new URL(caminho, import.meta.url), 'utf8')
await confere('useEntregasApi descarta leitura de geração velha', () => {
  const texto = fonte('../src/aplicacao/useEntregasApi.js')
  assert.match(texto, /geracao\.nova\(\)/)
  assert.match(texto, /geracao\.vale\(/)
})

// 5. contador em voo
await confere('contador em voo: ocupado até a última ação terminar', () => {
  const vistos = []
  const c = exige(contadorMod, 'criarContadorEmVoo')((n) => vistos.push(n))
  const a = c.iniciar()
  const b = c.iniciar()
  a()
  assert.equal(c.ocupado(), true, 'a primeira a terminar soltou os botões com outra em voo')
  a()
  assert.equal(c.ocupado(), true, 'soltar duas vezes a mesma ação não pode descontar a outra')
  b()
  assert.equal(c.ocupado(), false)
  assert.deepEqual(vistos, [1, 2, 1, 0])
})
await confere('useEntregasApi usa o contador, não um boolean', () => {
  const texto = fonte('../src/aplicacao/useEntregasApi.js')
  assert.doesNotMatch(texto, /setOcupado\(/)
  assert.match(texto, /criarContadorEmVoo/)
})

// 6. SSE das comandas
await confere('useComandasCliente não reabre o SSE a cada ação', () => {
  const texto = fonte('../src/aplicacao/useComandasCliente.js')
  const deps = texto.match(/\}, \[([^\]]*)\]\)/)?.[1] ?? ''
  assert.ok(deps.includes('conversaId'), 'deps do efeito não encontradas')
  assert.doesNotMatch(deps, /versao/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
