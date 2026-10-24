/* eslint-disable no-console */
// Prova da #2005: achados do console na banca de 23/10 (pacote 5). Cada conferência falhava no master
// 2df8874a6 pelo motivo descrito no título e passa depois da correção. Sem tela, sem navegador: os
// hooks rodam num hospedeiro mínimo (o mesmo desenho da prova-1577-telas-console), HTTP e SSE são
// trocados na fronteira e o tempo é o de `mock.timers`. As telas ficam na prova-2005-banca-console-telas.
//
//   01  Cobrar diferença não existe no modo API; sinal pago não é "item novo"
//   02  fechar o caixa relê o dia antes de abrir o modal e antes de confirmar
//   03  dossiê que falha avisa e tenta de novo
//   05  emissão de cobrança no modo API sai na hora e não se perde ao fechar a tela
//   06  navegador do tablet sem AbortSignal.any e sem toSorted
//   07  Chamar no WhatsApp: a conversa nova espera a lista conhecê-la
//   08  leitura em voo não desfaz Assumir/Encerrar
//   09  ver mensagens anteriores (antesDe)
//   19  uma conta de saldo, nomes que não colidem
//   20  Cozinha e Entregas conferem a fila mesmo com o SSE vivo
//   22  status que o console não conhece não libera cobrança
//   E10 "Pronto"/"Saiu"/"Entregue" pendentes saem quando a aba esconde ou fecha
//
//   node ferramentas/prova-2005-banca-console.mjs

import { registerHooks } from 'node:module'
import { mock } from 'node:test'
import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { join } from 'node:path'

// --- Hospedeiro de hooks (o mesmo da prova-1577-telas-console) -----------------------------------
let ativo = null
const depsIguais = (a, b) => a && b && a.length === b.length && a.every((v, i) => Object.is(v, b[i]))
const hooks = {
  useState(inicial) {
    const host = ativo, indice = host.indice++
    if (!(indice in host.valores)) host.valores[indice] = typeof inicial === 'function' ? inicial() : inicial
    return [host.valores[indice], (valor) => {
      const proximo = typeof valor === 'function' ? valor(host.valores[indice]) : valor
      if (!Object.is(proximo, host.valores[indice])) { host.valores[indice] = proximo; host.sujo = true }
    }]
  },
  useRef(inicial) {
    const host = ativo, indice = host.indice++
    return host.valores[indice] ??= { current: inicial }
  },
  useCallback(fn, deps) {
    const host = ativo, indice = host.indice++, anterior = host.valores[indice]
    if (!anterior || !depsIguais(anterior.deps, deps)) host.valores[indice] = { fn, deps }
    return host.valores[indice].fn
  },
  useEffect(fn, deps) {
    const host = ativo, indice = host.indice++, anterior = host.efeitos.get(indice)
    if (!anterior || !depsIguais(anterior.deps, deps)) {
      host.pendentes.push(() => { anterior?.limpar?.(); host.efeitos.set(indice, { deps, limpar: fn() }) })
    }
  },
}
class Hospedeiro {
  constructor(hook, props) {
    this.hook = hook; this.props = props; this.valores = []; this.efeitos = new Map(); this.sujo = true
    this.renderizar()
  }
  renderizar(props = this.props) {
    this.props = props
    let voltas = 0
    do {
      assert.ok(voltas++ < 20, 'efeitos não devem causar renderização sem fim')
      this.sujo = false; this.indice = 0; this.pendentes = []; ativo = this
      this.resultado = this.hook(this.props); ativo = null
      this.pendentes.forEach((efeito) => efeito())
    } while (this.sujo)
    return this.resultado
  }
  async estabilizar() {
    for (let i = 0; i < 80; i++) { await Promise.resolve(); if (this.sujo) this.renderizar() }
    return this.resultado
  }
  desmontar() { for (const efeito of this.efeitos.values()) efeito.limpar?.(); this.efeitos.clear() }
}

// --- Fronteiras falsas -------------------------------------------------------------------------
const chamadas = []
const respostas = []
const conexoes = []
const responder = (metodo, caminho, executar) => respostas.push({ metodo, caminho, executar })
globalThis.__prova2005 = {
  hooks,
  chamarApi(caminho, opcoes = {}) {
    const metodo = opcoes.metodo ?? 'GET'
    chamadas.push({ metodo, caminho, opcoes })
    const indice = respostas.findIndex((r) => r.metodo === metodo && caminho.startsWith(r.caminho))
    return indice < 0 ? Promise.resolve([]) : Promise.resolve().then(respostas.splice(indice, 1)[0].executar)
  },
  conectar(ouvinte) {
    const conexao = { ...ouvinte, fechada: false }
    conexoes.push(conexao)
    return () => { conexao.fechada = true }
  },
}
const moduloDados = (codigo) => ({ url: 'data:text/javascript,' + encodeURIComponent(codigo), shortCircuit: true })
const COM_HOOKS_FALSOS = /\/aplicacao\/use(?:EntregasApi|CozinhaApi|CaixaDoDiaApi|Emissao)\.js$/
registerHooks({
  resolve(especificador, contexto, proximo) {
    const pai = contexto.parentURL ?? ''
    if (especificador === 'react' && COM_HOOKS_FALSOS.test(pai)) {
      return moduloDados('const h=globalThis.__prova2005.hooks; export const useState=h.useState,useRef=h.useRef,useCallback=h.useCallback,useEffect=h.useEffect;')
    }
    if (especificador === './cliente' && /\/infra\/api\/(?:entregasApi|kdsApi|caixaApi)\.js$/.test(pai)) {
      return moduloDados('export const chamarApi=(...a)=>globalThis.__prova2005.chamarApi(...a);')
    }
    // Só as telas ouvem o SSE falso.
    if (especificador.endsWith('/eventosOperacao') && /\/aplicacao\//.test(pai)) {
      return moduloDados('export const conectarEventosOperacao=(o)=>globalThis.__prova2005.conectar(o);')
    }
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  // `fonteDados.js` lê `import.meta.env` do Vite; no node a fonte é fixa.
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    return proximo(url, contexto)
  },
})

// Navegador mínimo para os efeitos que escutam a janela e o documento.
globalThis.window = new EventTarget()
globalThis.document = Object.assign(new EventTarget(), { visibilityState: 'visible' })

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  chamadas.length = 0; respostas.length = 0; conexoes.length = 0
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  } finally { mock.timers.reset() }
}
const AGORA = Date.parse('2026-10-23T15:00:00Z')
const ligarRelogio = () => mock.timers.enable({ apis: ['setTimeout', 'setInterval', 'Date'], now: AGORA })
const andar = (ms) => mock.timers.tick(ms)
const umInstante = () => new Promise((resolver) => { setImmediate(resolver) })

// Módulos que esta entrega cria: antes dela não existem, e a conferência que depende deles falha dizendo isso.
const novo = async (caminho) => import(caminho).catch(() => ({}))
const existe = (modulo, nome) => assert.equal(typeof modulo[nome], 'function', `${nome} ainda não existe`)

// EasyStok de mentira no `fetch` (para as ações que falam pelo `chamarApi` de verdade).
globalThis.sessionStorage = {
  getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 600000, empresa: { id: 'empresa' }, usuario: { id: 'u-1', nome: 'Thati' } }),
  setItem() {}, removeItem() {},
}
const corpo = (data) => new Response(JSON.stringify({ data }), { status: 200 })
const erroHttp = (status, mensagem) => new Response(JSON.stringify({ error: { message: mensagem } }), { status })

const acao = await import('../src/aplicacao/acoes.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const { conversaDaApi, situacaoConversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { chamarApi } = await import('../src/infra/api/cliente.js')

const USUARIO = { id: 'u-1', nome: 'Thati' }
const BASE_MS = Date.parse('2026-10-23T14:50:00Z')
const hora = (minutosAtras) => new Date(BASE_MS - minutosAtras * 60000).toISOString()
const resumo = (id, extra = {}) => ({
  id, canal: 'WhatsApp', contatoNome: `Cliente ${id}`, contatoIdExterno: `55119${id}`, clienteId: null,
  situacao: 'Automatica', assumidaPorUsuarioId: null, naoLidas: 0, dentroDaJanela: true,
  ultimaMensagemEm: hora(0), ultimaMensagemTexto: 'oi', aguardaResposta: false,
  pedidoEmAndamentoId: null, ...extra,
})
const mensagem = (id, minutosAtras, extra = {}) => ({
  id, direcao: 'Entrada', autor: 'Cliente', tipoConteudo: 'Texto', texto: `mensagem ${id}`,
  enviadaEm: hora(minutosAtras), status: 'Recebida', ...extra,
})
const estadoCom = (...resumos) => estadoInicial({
  conversas: resumos.map((r) => conversaDaApi(r, [], USUARIO)), catalogo: { janelas: [] }, regras: [], modoAgente: 'simulado',
})
const sincronizar = (estado, resumos, extra = {}) =>
  reducer(estado, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: resumos.map((r) => conversaDaApi(r, [], USUARIO)), ...extra })

// ================================================================================================
// 06. Navegador do tablet: AbortSignal.any (Chrome 116, Safari 17.4) e toSorted fora do alvo do build
// ================================================================================================
await confere('06. sem AbortSignal.any a leitura com sinal e prazo ainda sai (não vira "Sem conexão")', async () => {
  const original = AbortSignal.any
  AbortSignal.any = undefined
  globalThis.fetch = async () => corpo('lido')
  try {
    const lido = await chamarApi('/api/x', { sinal: new AbortController().signal, prazoComCancelamento: true })
    assert.equal(lido, 'lido')
  } finally { AbortSignal.any = original }
})

await confere('06. o sinal combinado aborta quando qualquer um aborta, com ou sem AbortSignal.any', async () => {
  const { combinarSinais } = await novo('../src/infra/api/sinais.js')
  existe({ combinarSinais }, 'combinarSinais')
  const original = AbortSignal.any
  try {
    for (const nativo of [original, undefined]) {
      AbortSignal.any = nativo
      const [a, b] = [new AbortController(), new AbortController()]
      const junto = combinarSinais([a.signal, b.signal])
      assert.equal(junto.aborted, false)
      b.abort(new Error('motivo b'))
      assert.equal(junto.aborted, true, nativo ? 'nativo' : 'sem o nativo')
      assert.equal(junto.reason.message, 'motivo b')
      const jaAbortado = new AbortController()
      jaAbortado.abort()
      assert.equal(combinarSinais([new AbortController().signal, jaAbortado.signal]).aborted, true, 'um sinal já abortado')
    }
  } finally { AbortSignal.any = original }
})

await confere('06. sem Array.prototype.toSorted o último pagamento do pedido ainda sai', async () => {
  const { ultimoPagamentoApi } = await import('../src/dominio/pagamento.js')
  const original = Array.prototype.toSorted
  Array.prototype.toSorted = undefined
  try {
    assert.equal(ultimoPagamentoApi({ pagamentosApi: [{ em: 1, id: 'a' }, { em: 3, id: 'b' }, { em: 2, id: 'c' }] }).id, 'b')
  } finally { Array.prototype.toSorted = original }
})

await confere('06. nenhuma tela chama AbortSignal.any direto: passa por combinarSinais', () => {
  const raiz = fileURLToPath(new URL('../src/', import.meta.url))
  const arquivos = readdirSync(raiz, { recursive: true }).filter((n) => /\.(js|jsx)$/.test(n))
  const diretos = arquivos.filter((n) => !n.replaceAll('\\', '/').endsWith('infra/api/sinais.js')
    && /AbortSignal\.any\(/.test(readFileSync(join(raiz, n), 'utf8')))
  assert.deepEqual(diretos, [])
})

// ================================================================================================
// 19. Uma conta de saldo
// ================================================================================================
await confere('19. cada nome tem uma conta só: faltaPagar é do pedido, faltaDaCobranca é da cobrança', async () => {
  const cobranca = await import('../src/dominio/cobranca.js')
  const pagamento = await import('../src/dominio/pagamento.js')
  assert.equal(cobranca.faltaPagar, undefined, 'dois símbolos com o mesmo nome e contas diferentes')
  existe(cobranca, 'faltaDaCobranca')
  assert.equal(cobranca.faltaDaCobranca({ pagaEm: 1, valor: 50, valorPago: 30 }), 20)
  assert.equal(cobranca.faltaDaCobranca({ pagaEm: null, valor: 50, valorPago: null }), 0)
  existe(pagamento, 'faltaPagar')
})

await confere('19. saldoApi: total menos o alocado, em centavos exatos, nunca negativo', async () => {
  const { saldoApi } = await import('../src/dominio/pagamento.js')
  existe({ saldoApi }, 'saldoApi')
  assert.equal(saldoApi({ totalApi: 81, alocadoApi: 38 }), 43)
  assert.equal(saldoApi({ totalApi: 1.1, alocadoApi: 0.7 + 0.4 }), 0, 'a soma em ponto flutuante (1,0999…) não vira "falta R$ 0,00"')
  assert.equal(saldoApi({ totalApi: 50, totalPagoApi: 20 }), 30, 'API sem o alocado cai no recebido')
  assert.equal(saldoApi({ totalApi: 50, alocadoApi: 60 }), 0)
})

await confere('19. as telas usam o saldoApi em vez de refazer a conta', () => {
  const fonte = (arquivo) => readFileSync(new URL(`../src/${arquivo}`, import.meta.url), 'utf8')
  for (const arquivo of ['features/ficha-cliente/BarraProximoPasso.jsx', 'features/ficha-cliente/BlocoCobranca.jsx']) {
    assert.doesNotMatch(fonte(arquivo), /pedido\.totalApi - (alocado|pago)/, arquivo)
    assert.match(fonte(arquivo), /saldoApi/, arquivo)
  }
  assert.doesNotMatch(fonte('features/ficha-cliente/PainelFicha.jsx'), /faltaPagar as saldoDoPedido/, 'alias que escondia a colisão de nomes')
})

// ================================================================================================
// 01. Cobrar diferença
// ================================================================================================
const SINAL_PAGO = {
  pedidoId: '11111111-2222-3333-4444-555555555555', status: 'aguardando', total: 65, frete: 5, itens: [],
  totalPago: 32.5, alocado: 32.5, sinalNaReserva: true,
  pagamentos: [{ id: 'pg1', valor: 32.5, metodo: 'pix', pagoEm: '2026-10-23T14:00:00Z' }],
  cobranca: {
    cobrancaId: 'cb1', provedor: 'mercadopago', status: 'Paga', valor: 32.5, valorPago: 32.5, tentativa: 1,
    criadaEm: '2026-10-23T13:50:00Z', pagaEm: '2026-10-23T14:00:00Z', linkPagamento: 'https://pagamento.exemplo/cb1',
  },
}

await confere('01. Cobrar diferença não aparece no modo API (a rota não existe); na demonstração continua', async () => {
  const { acaoDisponivel } = await import('../src/aplicacao/api/naoLigadas.js')
  assert.equal(acaoDisponivel('cobrarDiferenca', { fonteApi: true }), false)
  assert.equal(acaoDisponivel('cobrarDiferenca', { fonteApi: false }), true)
})

await confere('01. sinal pago de encomenda: o restante da entrega não é "item novo depois do pagamento"', async () => {
  const { diferencaAposPagamento, restanteDoSinal } = await import('../src/dominio/pagamento.js')
  const pedido = pedidoDaApi(SINAL_PAGO)
  assert.equal(diferencaAposPagamento(pedido, []), 0, 'o restante já foi combinado para a entrega')
  existe({ restanteDoSinal }, 'restanteDoSinal')
  assert.equal(restanteDoSinal(pedido), 32.5)
  // Controle: sem sinal combinado, o que passa do pago continua sendo item novo.
  const itemNovo = pedidoDaApi({ ...SINAL_PAGO, sinalNaReserva: false, total: 90, totalPago: 65, alocado: 65,
    cobranca: { ...SINAL_PAGO.cobranca, valor: 65, valorPago: 65 } })
  assert.equal(diferencaAposPagamento(itemNovo, []), 25)
  assert.equal(restanteDoSinal(itemNovo), 0)
})

await confere('01. reenviarCobranca com valor forçado no modo API avisa em vez de cobrar outra coisa', async () => {
  const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
  const pedido = pedidoDaApi({ ...SINAL_PAGO, status: 'aguardando_pagamento', sinalNaReserva: false, totalPago: 0, alocado: 0,
    pagamentos: [], cobranca: { ...SINAL_PAGO.cobranca, status: 'Pendente', pagaEm: null, valorPago: null,
      expiraEm: new Date(Date.now() + 1800000).toISOString() } })
  const despachados = []
  const buscas = []
  globalThis.fetch = async (url, opcoes) => { buscas.push({ url, ...opcoes }); return corpo(null) }
  const acoes = criarAcoesComandaApi({}, { despachar: (a) => despachados.push(a), estadoRef: { current: { conversas: [{ id: 'c1', pedido }] } } })
  await acoes.reenviarCobranca('c1', pedido, 12.5)
  assert.equal(buscas.length, 0, 'nenhuma chamada: o valor forçado não tem rota')
  assert.equal(despachados.length, 1)
  assert.match(despachados[0].mensagem, /Cobrar diferença/)
})

// ================================================================================================
// 02. Fechar o caixa relê o dia
// ================================================================================================
const DIA_DO_CAIXA = {
  data: '2026-10-23', saldoInicial: 100, totalVendas: 0, totalPagamentosPedidos: 0, totalEntradasExtras: 0, totalSaidasExtras: 0,
  saldoEsperado: 100, aberto: true, fechado: false, movimentos: [], linhasExtras: [],
}

await confere('02. recarregar do caixa devolve o dia lido (ou nulo se a leitura falhou)', async () => {
  const { useCaixaDoDiaApi } = await import('../src/aplicacao/useCaixaDoDiaApi.js')
  responder('GET', '/api/caixa/dia', () => DIA_DO_CAIXA)
  responder('GET', '/api/caixa/fechamentos', () => [])
  const h = new Hospedeiro(useCaixaDoDiaApi, { fechamentos: 7 })
  try {
    await h.estabilizar()
    responder('GET', '/api/caixa/dia', () => ({ ...DIA_DO_CAIXA, saldoInicial: 150 }))
    responder('GET', '/api/caixa/fechamentos', () => [])
    const lido = await h.resultado.recarregar()
    assert.equal(lido?.saldoInicial, 150)
    responder('GET', '/api/caixa/dia', () => { throw new Error('sem rede') })
    assert.equal(await h.resultado.recarregar(), null)
    await h.estabilizar()
    assert.equal(h.resultado.estado, 'erro')
    assert.equal(h.resultado.dia?.saldoInicial, 150, 'a tela segue com o último dia lido')
  } finally { h.desmontar() }
})

await confere('02. relerParaFechar: falha na leitura segura o fechamento; esperado que mudou volta com o valor novo', async () => {
  const { relerParaFechar } = await novo('../src/aplicacao/fechamentoDoCaixa.js')
  existe({ relerParaFechar }, 'relerParaFechar')
  let leituras = 0
  const lendo = (dia) => async () => { leituras += 1; return dia }
  const semResposta = await relerParaFechar(lendo(null))
  assert.match(semResposta.erro, /conferir o caixa/)
  assert.equal(semResposta.dia, undefined)
  const aoAbrir = await relerParaFechar(lendo(DIA_DO_CAIXA))
  assert.equal(aoAbrir.erro, undefined)
  assert.equal(aoAbrir.gaveta.naGaveta, 100)
  // Ela abriu o modal com R$ 100,00 e entrou R$ 50,00 em dinheiro enquanto contava.
  const entrouDinheiro = { ...DIA_DO_CAIXA, linhasExtras: [{ em: '2026-10-23T15:00:00Z', tipo: 'entrada', valor: 50, meio: 'dinheiro', origem: 'pedido' }] }
  const aoConfirmar = await relerParaFechar(lendo(entrouDinheiro), { esperadoVisto: 100 })
  assert.match(aoConfirmar.erro, /150,00/)
  assert.equal(aoConfirmar.gaveta.naGaveta, 150)
  const igual = await relerParaFechar(lendo(DIA_DO_CAIXA), { esperadoVisto: 100 })
  assert.equal(igual.erro, undefined)
  assert.equal(leituras, 4, 'uma releitura por chamada')
})

await confere('02. a aba Caixa relê antes de abrir o modal e ao confirmar', () => {
  const tela = readFileSync(new URL('../src/features/gestao/caixa/AbaCaixaApi.jsx', import.meta.url), 'utf8')
  assert.equal((tela.match(/relerParaFechar\(/g) ?? []).length, 2, 'ao abrir e ao confirmar')
  assert.doesNotMatch(tela, /fecharCaixa\(agora, contado, gaveta\.naGaveta\)/, 'o esperado gravado vinha da carga antiga')
})

// ================================================================================================
// 03. Dossiê que falha
// ================================================================================================
const CLIENTE_DA_FICHA = '33333333-3333-3333-3333-333333333333'
const DOSSIE = { cliente: { id: CLIENTE_DA_FICHA, nome: 'Ana', telefone: '+5511999990001' }, enderecos: [], totalPedidos: 0, notas: [], ultimosPedidos: [] }

await confere('03. dossiê que falha avisa a atendente e tenta de novo enquanto a conversa segue aberta', async () => {
  ligarRelogio()
  const { criarAcoesClienteApi } = await import('../src/aplicacao/api/cliente.js')
  const conversa = { id: 'c1', clienteId: CLIENTE_DA_FICHA, canal: 'WhatsApp', cliente: { tags: [], notas: [] } }
  const estado = { conversas: [conversa], selecionadaId: 'c1' }
  const despachados = []
  let leituras = 0
  globalThis.fetch = async (url) => {
    if (!url.endsWith('/c1/dossie')) return corpo(null)
    leituras += 1
    return leituras === 1 ? erroHttp(503, 'Fora do ar') : corpo(DOSSIE)
  }
  const acoes = criarAcoesClienteApi({
    estadoRef: { current: estado },
    despachar: (a) => {
      despachados.push(a)
      if (a.tipo === acao.CLIENTE_DA_API) estado.conversas = [{ ...conversa, cliente: { ...conversa.cliente, daApi: true } }]
    },
  })
  await acoes.carregarClienteDaConversa('c1')
  assert.equal(leituras, 1)
  const aviso = despachados.find((a) => a.tipo === acao.AVISO_API)
  assert.match(aviso?.mensagem ?? '', /ficha do cliente/, 'a falha precisa aparecer')
  andar(8000)
  await umInstante()
  await umInstante()
  assert.equal(leituras, 2, 'uma nova tentativa depois da espera')
  assert.equal(despachados.filter((a) => a.tipo === acao.CLIENTE_DA_API).length, 1, 'o dossiê chegou')
  andar(60000)
  await umInstante()
  assert.equal(leituras, 2, 'ficha lida: sem mais tentativas')
})

await confere('03. sem a conversa aberta ou depois de algumas falhas, a tentativa para', async () => {
  ligarRelogio()
  const { criarAcoesClienteApi } = await import('../src/aplicacao/api/cliente.js')
  const conversa = { id: 'c1', clienteId: CLIENTE_DA_FICHA, canal: 'WhatsApp', cliente: { tags: [], notas: [] } }
  const estado = { conversas: [conversa], selecionadaId: 'c1' }
  let leituras = 0
  globalThis.fetch = async () => { leituras += 1; return erroHttp(503, 'Fora do ar') }
  const acoes = criarAcoesClienteApi({ estadoRef: { current: estado }, despachar() {} })
  await acoes.carregarClienteDaConversa('c1')
  for (let i = 0; i < 10; i += 1) { andar(8000); await umInstante(); await umInstante() }
  assert.equal(leituras, 5, 'a primeira e quatro novas tentativas')
  leituras = 0
  estado.selecionadaId = 'c1'
  await acoes.carregarClienteDaConversa('c1')
  estado.selecionadaId = 'outra'
  andar(8000); await umInstante(); await umInstante()
  assert.equal(leituras, 1, 'quem trocou de conversa não puxa o dossiê da anterior')
})

await confere('03. ficha já carregada (releitura pelo SSE) que falha fica quieta: sem aviso e sem nova tentativa', async () => {
  ligarRelogio()
  const { criarAcoesClienteApi } = await import('../src/aplicacao/api/cliente.js')
  const conversa = { id: 'c1', clienteId: CLIENTE_DA_FICHA, canal: 'WhatsApp', cliente: { tags: [], notas: [], daApi: true } }
  const despachados = []
  let leituras = 0
  globalThis.fetch = async () => { leituras += 1; return erroHttp(503, 'Fora do ar') }
  const acoes = criarAcoesClienteApi({ estadoRef: { current: { conversas: [conversa], selecionadaId: 'c1' } }, despachar: (a) => despachados.push(a) })
  await acoes.carregarClienteDaConversa('c1')
  assert.deepEqual(despachados, [], 'a Ficha na tela segue valendo: nada a avisar')
  andar(60000)
  await umInstante()
  assert.equal(leituras, 1, 'e não há o que tentar de novo')
})

// ================================================================================================
// 05. Emissão de cobrança
// ================================================================================================
const emissao = async () => {
  const { useEmissao } = await novo('../src/aplicacao/useEmissao.js')
  existe({ useEmissao }, 'useEmissao')
  return useEmissao
}

await confere('05. modo API: a ação sai na hora, uma vez, e o clique repetido no meio é ignorado', async () => {
  ligarRelogio()
  const useEmissao = await emissao()
  let saidas = 0
  let soltar
  const h = new Hospedeiro(({ aoEmitir, semEspera }) => useEmissao(aoEmitir, { semEspera }), {
    semEspera: true, aoEmitir: () => { saidas += 1; return new Promise((resolver) => { soltar = resolver }) },
  })
  try {
    h.resultado.emitir('x')
    h.resultado.emitir('x')
    assert.equal(saidas, 1, 'na hora, sem os 700 ms de espera')
    await h.estabilizar()
    assert.equal(h.resultado.emitindo, true)
    soltar()
    await h.estabilizar()
    assert.equal(h.resultado.emitindo, false)
  } finally { h.desmontar() }
})

await confere('05. demonstração: espera 700 ms; fechar a tela nesse intervalo não perde a ação', async () => {
  ligarRelogio()
  const useEmissao = await emissao()
  let saidas = 0
  const h = new Hospedeiro(({ aoEmitir }) => useEmissao(aoEmitir, { semEspera: false }), { aoEmitir: () => { saidas += 1 } })
  h.resultado.emitir()
  andar(699)
  assert.equal(saidas, 0)
  andar(1)
  assert.equal(saidas, 1)
  h.desmontar()
  const outro = new Hospedeiro(({ aoEmitir }) => useEmissao(aoEmitir, { semEspera: false }), { aoEmitir: () => { saidas += 1 } })
  outro.resultado.emitir()
  outro.desmontar()
  assert.equal(saidas, 2, 'a Ficha fechou com a emissão pendente: ela sai em vez de sumir')
  andar(5000)
  assert.equal(saidas, 2, 'e só uma vez')
})

await confere('05. a Ficha não guarda mais a espera fixa dentro do BlocoCobranca', () => {
  const tela = readFileSync(new URL('../src/features/ficha-cliente/BlocoCobranca.jsx', import.meta.url), 'utf8')
  assert.doesNotMatch(tela, /MS_DE_EMISSAO|function useEmissao/)
  assert.match(tela, /useEmissao\(.*semEspera: fonteApi/)
})

// ================================================================================================
// 07. Chamar no WhatsApp: a conversa nova ainda não está na lista
// ================================================================================================
await confere('07. selecionar uma conversa que o estado ainda não conhece espera a volta que a traz', () => {
  const base = estadoCom(resumo('a'), resumo('b'))
  let estado = reducer(base, { tipo: acao.SELECIONAR_CONVERSA, id: 'nova' })
  assert.equal(estado.selecionadaId, 'nova')
  // A volta que saiu antes de a conversa existir chega depois da seleção.
  estado = sincronizar(estado, [resumo('a'), resumo('b')])
  assert.equal(estado.selecionadaId, 'nova', 'não volta para a primeira da lista')
  estado = sincronizar(estado, [resumo('nova'), resumo('a'), resumo('b')])
  assert.equal(estado.selecionadaId, 'nova')
  assert.equal(estado.selecaoPendente ?? null, null, 'a espera acaba quando a conversa aparece')
})

await confere('07. conversa que nunca aparece não prende a seleção: depois de 3 voltas volta para a primeira', () => {
  let estado = reducer(estadoCom(resumo('a'), resumo('b')), { tipo: acao.SELECIONAR_CONVERSA, id: 'fantasma' })
  for (let volta = 1; volta <= 3; volta += 1) {
    estado = sincronizar(estado, [resumo('a'), resumo('b')])
    assert.equal(estado.selecionadaId, 'fantasma', `volta ${volta}`)
  }
  estado = sincronizar(estado, [resumo('a'), resumo('b')])
  assert.equal(estado.selecionadaId, 'a')
})

await confere('07. selecionar uma conversa que existe não deixa espera nenhuma', () => {
  const estado = reducer(estadoCom(resumo('a'), resumo('b')), { tipo: acao.SELECIONAR_CONVERSA, id: 'b' })
  assert.equal(estado.selecionadaId, 'b')
  assert.equal(estado.selecaoPendente ?? null, null)
  assert.equal(sincronizar(estado, [resumo('a')]).selecionadaId, 'a', 'conversa que sumiu da lista continua saindo da seleção')
})

// ================================================================================================
// 08. Leitura em voo não desfaz Assumir/Encerrar
// ================================================================================================
const relogio = async () => {
  const { proximoInstante } = await novo('../src/aplicacao/relogioDeLeitura.js')
  existe({ proximoInstante }, 'proximoInstante')
  return proximoInstante
}
const ASSUMIDA = resumo('a', { situacao: 'Assumida', assumidaPorUsuarioId: USUARIO.id, assumidaPorNome: USUARIO.nome })

await confere('08. resumo lido antes da confirmação não devolve o responsável antigo', () => {
  let estado = estadoCom(resumo('a'))
  estado = reducer(estado, { tipo: acao.CONFIRMAR_RESPONSAVEL_API, id: 'a', confirmadaEm: 200, situacao: situacaoConversaDaApi(ASSUMIDA, USUARIO) })
  assert.equal(estado.conversas[0].responsavel, USUARIO.nome)
  // A volta saiu em 100, antes do toque, e traz a conversa como era.
  const atrasado = sincronizar(estado, [resumo('a')], { lidaEm: { a: 100 } })
  const c = atrasado.conversas[0]
  assert.equal(c.responsavel, USUARIO.nome, 'o botão não volta a "Assumir"')
  assert.equal(c.estado, 'Em atendimento')
  assert.equal(c.assumidaPorUsuarioId, USUARIO.id)
  assert.ok(atrasado.automaticoPausado.a, 'o automático segue pausado')
})

await confere('08. a leitura que saiu depois da confirmação vale, inclusive quando outro atendente mudou a conversa', () => {
  let estado = estadoCom(resumo('a'))
  estado = reducer(estado, { tipo: acao.CONFIRMAR_RESPONSAVEL_API, id: 'a', confirmadaEm: 200, situacao: situacaoConversaDaApi(ASSUMIDA, USUARIO) })
  const devolvida = sincronizar(estado, [resumo('a')], { lidaEm: { a: 300 } })
  assert.equal(devolvida.conversas[0].responsavel, null, 'o servidor é a verdade depois da confirmação')
  assert.equal('confirmadaEm' in devolvida.conversas[0], false, 'a marca cai na primeira leitura nova')
  // Sem o carimbo da leitura (ação de outra origem) a regra de antes continua: o servidor manda.
  const semCarimbo = sincronizar(estado, [resumo('a')])
  assert.equal(semCarimbo.conversas[0].responsavel, null)
})

await confere('08. Encerrar também: a volta em voo não reabre a conversa por 5 s', () => {
  let estado = estadoCom(ASSUMIDA)
  estado = reducer(estado, { tipo: acao.ENCERRAR_ATENDIMENTO, id: 'a', confirmadaEm: 200 })
  assert.equal(estado.conversas[0].estado, 'Encerrado')
  assert.equal(sincronizar(estado, [ASSUMIDA], { lidaEm: { a: 150 } }).conversas[0].estado, 'Encerrado')
  assert.equal(sincronizar(estado, [{ ...ASSUMIDA, situacao: 'Encerrada' }], { lidaEm: { a: 250 } }).conversas[0].estado, 'Encerrado')
})

await confere('08. o laço carimba cada leitura com o instante em que ela saiu, e o estado respeita a confirmação', async () => {
  const proximoInstante = await relogio()
  const { criarCicloDeSincronizacao } = await import('../src/aplicacao/cicloDeSincronizacao.js')
  let estado = estadoCom()
  const inbox = { itens: [resumo('a')] }
  let seguraInbox = null
  globalThis.fetch = async (url) => {
    if (/\/conversas\?limite=/.test(url)) {
      if (seguraInbox) return seguraInbox.then(() => corpo(inbox.itens))
      return corpo(inbox.itens)
    }
    if (/\/mensagens/.test(url)) return corpo([])
    return corpo(null)
  }
  const volta = criarCicloDeSincronizacao({
    despachar: (a) => { estado = reducer(estado, a) }, lerUsuario: () => USUARIO, lerSelecionada: () => 'a', dono: USUARIO.id,
  })
  await volta()
  assert.equal(estado.conversas[0].responsavel, null)
  // Uma volta sai com a conversa livre e, antes de ela voltar, a atendente assume.
  let liberar
  seguraInbox = new Promise((resolver) => { liberar = resolver })
  const emVoo = volta()
  await umInstante()
  estado = reducer(estado, { tipo: acao.CONFIRMAR_RESPONSAVEL_API, id: 'a', confirmadaEm: proximoInstante(), situacao: situacaoConversaDaApi(ASSUMIDA, USUARIO) })
  liberar()
  await emVoo
  assert.equal(estado.conversas[0].responsavel, USUARIO.nome, 'a volta que saiu antes não desfaz o toque')
  // A volta seguinte saiu depois e confirma a conversa assumida.
  seguraInbox = null
  inbox.itens = [ASSUMIDA]
  await volta()
  assert.equal(estado.conversas[0].responsavel, USUARIO.nome)
  assert.equal('confirmadaEm' in estado.conversas[0], false)
})

// ================================================================================================
// 09. Ver mensagens anteriores
// ================================================================================================
const cem = (de = 0) => Array.from({ length: 100 }, (_, i) => mensagem(`m-${de + i}`, 1000 - de - i))

await confere('09. listarMensagens leva o cursor antesDe à API', async () => {
  const { listarMensagens } = await import('../src/infra/api/conversasApi.js')
  const urls = []
  globalThis.fetch = async (url) => { urls.push(url); return corpo([]) }
  await listarMensagens('c1')
  await listarMensagens('c1', { antesDe: '2026-10-20T10:00:00Z' })
  assert.equal(urls[0], '/api/atendimento/conversas/c1/mensagens?limite=100')
  assert.equal(urls[1], '/api/atendimento/conversas/c1/mensagens?limite=100&antesDe=2026-10-20T10%3A00%3A00Z')
})

await confere('09. conversa com a página cheia (100) oferece as anteriores; com menos, não', () => {
  assert.equal(conversaDaApi(resumo('a'), cem(), USUARIO).temAnteriores, true)
  assert.equal(conversaDaApi(resumo('a'), cem().slice(0, 99), USUARIO).temAnteriores, false)
  assert.equal(conversaDaApi(resumo('a'), [], USUARIO).temAnteriores, false)
})

await confere('09. a ação busca antes da mais antiga e funde no fio; a volta de 5 s não apaga o que veio', async () => {
  const { criarAcoesMensagensAnterioresApi } = await novo('../src/aplicacao/api/mensagensAnteriores.js')
  existe({ criarAcoesMensagensAnterioresApi }, 'criarAcoesMensagensAnterioresApi')
  const recentes = cem(100)
  let estado = reducer(estadoCom(resumo('a')), { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [conversaDaApi(resumo('a'), recentes, USUARIO)] })
  assert.equal(estado.conversas[0].mensagens.length, 100)
  const urls = []
  const antigas = Array.from({ length: 30 }, (_, i) => mensagem(`velha-${i}`, 5000 - i))
  globalThis.fetch = async (url) => { urls.push(url); return corpo(antigas) }
  const despachar = (a) => { estado = reducer(estado, a) }
  const acoes = criarAcoesMensagensAnterioresApi({ despachar, estadoRef: { get current() { return estado } } })
  assert.equal(await acoes.carregarMensagensAnteriores('a'), true)
  assert.equal(urls.length, 1)
  assert.match(urls[0], new RegExp(`antesDe=${encodeURIComponent(conversaDaApi(resumo('a'), recentes, USUARIO).mensagens[0].em).replaceAll('.', '\\.')}`))
  let c = estado.conversas[0]
  assert.equal(c.mensagens.length, 130)
  assert.equal(c.mensagens[0].id, 'velha-0', 'as anteriores entram no topo, em ordem')
  assert.equal(c.anterioresEsgotadas, true, 'veio menos que uma página: não há mais')
  // A volta seguinte traz de novo só as 100 mais recentes.
  estado = reducer(estado, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [conversaDaApi(resumo('a'), recentes, USUARIO)] })
  c = estado.conversas[0]
  assert.equal(c.mensagens.length, 130, 'o que a atendente já abriu fica')
  assert.equal(c.mensagens.filter((m) => m.id === 'm-100').length, 1, 'sem duplicar')
  // Volta que não leu as mensagens (semMensagens) também não perde as anteriores.
  estado = reducer(estado, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [conversaDaApi(resumo('a'), null, USUARIO)], semMensagens: ['a'] })
  assert.equal(estado.conversas[0].mensagens.length, 130)
  assert.equal(estado.conversas[0].temAnteriores, true)
  estado = reducer(estado, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [conversaDaApi(resumo('a'), recentes, USUARIO)] })
  assert.equal(estado.conversas[0].mensagens.length, 130)
})

await confere('09. saída antiga que o servidor marca Falhou ou Pendente entra no topo e fica lá, sem ir para o fim do fio', async () => {
  const { criarAcoesMensagensAnterioresApi } = await novo('../src/aplicacao/api/mensagensAnteriores.js')
  existe({ criarAcoesMensagensAnterioresApi }, 'criarAcoesMensagensAnterioresApi')
  const recentes = cem(100)
  let estado = reducer(estadoCom(resumo('a')), { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [conversaDaApi(resumo('a'), recentes, USUARIO)] })
  const saida = (id, minutosAtras, status) => mensagem(id, minutosAtras, { direcao: 'Saida', autor: 'Atendente', status })
  const antigas = [saida('velha-falhou', 5000, 'Falhou'), saida('velha-pendente', 4999, 'Pendente'), mensagem('velha-ok', 4998)]
  globalThis.fetch = async () => corpo(antigas)
  const acoes = criarAcoesMensagensAnterioresApi({ despachar: (a) => { estado = reducer(estado, a) }, estadoRef: { get current() { return estado } } })
  assert.equal(await acoes.carregarMensagensAnteriores('a'), true)
  const ordem = () => estado.conversas[0].mensagens.map((m) => m.id)
  assert.deepEqual(ordem().slice(0, 3), ['velha-falhou', 'velha-pendente', 'velha-ok'])
  for (let volta = 0; volta < 2; volta += 1) {
    estado = reducer(estado, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [conversaDaApi(resumo('a'), recentes, USUARIO)] })
    assert.deepEqual(ordem().slice(0, 3), ['velha-falhou', 'velha-pendente', 'velha-ok'], `volta ${volta + 1}: continuam no topo`)
    assert.equal(ordem().length, 103, 'sem duplicar')
    assert.equal(ordem().at(-1), 'm-199', 'nada de saída antiga no fim do fio, onde ganharia o Reenviar')
  }
})

await confere('09. duas pedidas ao mesmo tempo viram uma, e a falha avisa sem apagar nada', async () => {
  const { criarAcoesMensagensAnterioresApi } = await novo('../src/aplicacao/api/mensagensAnteriores.js')
  existe({ criarAcoesMensagensAnterioresApi }, 'criarAcoesMensagensAnterioresApi')
  let estado = reducer(estadoCom(resumo('a')), { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [conversaDaApi(resumo('a'), cem(), USUARIO)] })
  const despachados = []
  let pedidas = 0
  let soltar
  globalThis.fetch = async () => { pedidas += 1; await new Promise((resolver) => { soltar = resolver }); return erroHttp(503, 'Fora do ar') }
  const acoes = criarAcoesMensagensAnterioresApi({
    despachar: (a) => { despachados.push(a); estado = reducer(estado, a) }, estadoRef: { get current() { return estado } },
  })
  const [a, b] = [acoes.carregarMensagensAnteriores('a'), acoes.carregarMensagensAnteriores('a')]
  await umInstante()
  soltar()
  assert.deepEqual(await Promise.all([a, b]), [false, false])
  assert.equal(pedidas, 1)
  assert.equal(despachados.filter((d) => d.tipo === acao.AVISO_API).length, 1)
  assert.equal(estado.conversas[0].mensagens.length, 100)
})

// ================================================================================================
// 20. Cozinha e Entregas conferem a fila com o SSE vivo
// ================================================================================================
const leituras = () => chamadas.filter((c) => c.metodo === 'GET')
const daFila = () => leituras().filter((c) => c.caminho.startsWith('/api/kds/pedidos'))
async function montar(hook, props, { aoVivo = true } = {}) {
  ligarRelogio()
  const h = new Hospedeiro(hook, props)
  await h.estabilizar()
  if (aoVivo) {
    conexoes.at(-1).aoEvento({ evento: 'ready' })
    andar(300)
    await h.estabilizar()
  }
  chamadas.length = 0
  return h
}
const { useEntregasApi } = await import('../src/aplicacao/useEntregasApi.js')
const { useCozinhaApi } = await import('../src/aplicacao/useCozinhaApi.js')
const { SEGUNDOS_PARA_DESFAZER_ETAPA, chaveDaEntrega } = await import('../src/aplicacao/envioComDesfazer.js')
const PRAZO = SEGUNDOS_PARA_DESFAZER_ETAPA * 1000
const escritas = () => chamadas.filter((c) => c.metodo !== 'GET')

await confere('20. Cozinha com o SSE vivo e mudo ainda confere a fila a cada 30 s', async () => {
  const h = await montar(useCozinhaApi, { data: null })
  try {
    // `montar` já andou 300 ms depois do `ready`: a conferência vence aos 30 s do `ready`.
    andar(29699)
    await h.estabilizar()
    assert.equal(daFila().length, 0)
    andar(1)
    await h.estabilizar()
    assert.equal(daFila().length, 1, 'uma conferência')
    andar(30000)
    await h.estabilizar()
    assert.equal(daFila().length, 2, 'e outra 30 s depois')
  } finally { h.desmontar() }
})

await confere('20. Cozinha: SSE caído lê a cada 15 s; ao voltar, a conferência de 30 s substitui a de 15 s', async () => {
  const h = await montar(useCozinhaApi, { data: null })
  try {
    conexoes.at(-1).aoCair()
    andar(15000)
    await h.estabilizar()
    assert.equal(daFila().length, 1, 'caído: a cada 15 s')
    chamadas.length = 0
    andar(10000)
    const nova = conexoes.at(-1)
    nova.aoEvento({ evento: 'ready' })
    andar(300)
    await h.estabilizar()
    chamadas.length = 0
    andar(15000)
    await h.estabilizar()
    assert.equal(daFila().length, 0, 'de volta ao vivo não lê a cada 15 s')
    andar(15000)
    await h.estabilizar()
    assert.equal(daFila().length, 1)
  } finally { h.desmontar() }
})

await confere('20. Entregas com o SSE vivo e mudo confere a operação a cada 30 s', async () => {
  const h = await montar(useEntregasApi)
  try {
    andar(29699)
    await h.estabilizar()
    assert.equal(leituras().length, 0)
    andar(1)
    await h.estabilizar()
    assert.equal(leituras().length, 4, 'só a operação, sem o cadastro')
  } finally { h.desmontar() }
})

await confere('20. a constante de conferência é uma só (recargaAoVivo) para Balcão, Cozinha e Entregas', () => {
  for (const arquivo of ['aplicacao/useCozinhaApi.js', 'aplicacao/useEntregasApi.js']) {
    const fonte = readFileSync(new URL(`../src/${arquivo}`, import.meta.url), 'utf8')
    assert.doesNotMatch(fonte, /const (POLLING_SEM_SSE_MS|RECONECTAR_MS) =/, arquivo)
    assert.match(fonte, /from '\.\/recargaAoVivo'/, arquivo)
  }
})

// ================================================================================================
// E10. Pendente do "Desfazer" sai quando a aba esconde ou fecha
// ================================================================================================
const aoEsconder = () => {
  globalThis.document.visibilityState = 'hidden'
  globalThis.document.dispatchEvent(new Event('visibilitychange'))
}

await confere('E10. Cozinha: fechar a aba (pagehide) com "Pronto" pendente manda o toque na hora, com keepalive', async () => {
  const h = await montar(useCozinhaApi, { data: null })
  try {
    h.resultado.avancar('p-1', 'pronto')
    await h.estabilizar()
    assert.equal(escritas().length, 0, 'ainda na janela de Desfazer')
    globalThis.window.dispatchEvent(new Event('pagehide'))
    await h.estabilizar()
    assert.equal(escritas().length, 1)
    assert.equal(escritas()[0].caminho, '/api/kds/pedidos/p-1/status')
    assert.equal(escritas()[0].opcoes.keepalive, true, 'a requisição sobrevive à aba fechando')
  } finally { h.desmontar() }
})

await confere('E10. Cozinha: o tablet que suspende (aba escondida) também manda o pendente', async () => {
  const h = await montar(useCozinhaApi, { data: null })
  try {
    h.resultado.avancar('p-2', 'pronto')
    aoEsconder()
    await h.estabilizar()
    assert.equal(escritas().length, 1)
    // A aba voltou: nada pendente, nada a mandar.
    globalThis.document.visibilityState = 'visible'
    globalThis.document.dispatchEvent(new Event('visibilitychange'))
    andar(PRAZO)
    await h.estabilizar()
    assert.equal(escritas().length, 1, 'não manda duas vezes')
  } finally { globalThis.document.visibilityState = 'visible'; h.desmontar() }
})

await confere('E10. Entregas: "Entregue" pendente sai com a aba escondida', async () => {
  const h = await montar(useEntregasApi)
  try {
    h.resultado.acoes.entregue('v-1', 'p-1')
    await h.estabilizar()
    assert.equal(escritas().length, 0)
    aoEsconder()
    await h.estabilizar()
    assert.equal(escritas().length, 1)
    assert.equal(escritas()[0].opcoes.keepalive, true)
  } finally { globalThis.document.visibilityState = 'visible'; h.desmontar() }
})

await confere('E10. ao sair da tela os ouvintes da janela são retirados', async () => {
  const marcas = []
  const [adicionar, remover] = [globalThis.window.addEventListener, globalThis.window.removeEventListener]
  globalThis.window.addEventListener = function (tipo, ...resto) { marcas.push(['+', tipo]); return adicionar.call(this, tipo, ...resto) }
  globalThis.window.removeEventListener = function (tipo, ...resto) { marcas.push(['-', tipo]); return remover.call(this, tipo, ...resto) }
  try {
    const h = await montar(useCozinhaApi, { data: null })
    h.desmontar()
    assert.ok(marcas.some(([m, t]) => m === '+' && t === 'pagehide'), 'escutou pagehide')
    assert.ok(marcas.some(([m, t]) => m === '-' && t === 'pagehide'), 'e parou de escutar')
  } finally { globalThis.window.addEventListener = adicionar; globalThis.window.removeEventListener = remover }
})

await confere('E10. chamarApi leva keepalive ao fetch quando pedido', async () => {
  let init = null
  globalThis.fetch = async (_url, opcoes) => { init = opcoes; return corpo(null) }
  await chamarApi('/api/x', { metodo: 'PATCH', corpo: { a: 1 }, keepalive: true })
  assert.equal(init.keepalive, true)
  await chamarApi('/api/x', { metodo: 'PATCH', corpo: { a: 1 } })
  assert.notEqual(init.keepalive, true)
})

// ================================================================================================
// 22. Status que o console não conhece
// ================================================================================================
const PEDIDO_NOVO = { pedidoId: '99999999-9999-9999-9999-999999999999', total: 50, frete: 0, itens: [], totalPago: 0 }

await confere('22. os 10 status do servidor continuam conhecidos; um novo vem marcado como desconhecido', () => {
  for (const status of ['rascunho', 'aguardando_pagamento', 'aguardando_aprovacao_baba', 'aprovado_baba', 'aguardando', 'preparando', 'pronto', 'saiu_para_entrega', 'entregue', 'cancelado']) {
    assert.equal(pedidoDaApi({ ...PEDIDO_NOVO, status }).statusDesconhecido, false, status)
  }
  const desconhecido = pedidoDaApi({ ...PEDIDO_NOVO, status: 'em_analise_de_fraude' })
  assert.equal(desconhecido.statusDesconhecido, true)
  assert.equal(desconhecido.statusApi, 'em_analise_de_fraude', 'o status cru fica à vista')
})

await confere('22. pedido de status desconhecido: gerar cobrança e dar baixa avisam e não chamam a API', async () => {
  const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
  const pedido = pedidoDaApi({ ...PEDIDO_NOVO, status: 'em_analise_de_fraude' })
  const despachados = []
  const buscas = []
  globalThis.fetch = async (url, opcoes) => { buscas.push({ url, ...opcoes }); return corpo(null) }
  const acoes = criarAcoesComandaApi({}, { despachar: (a) => despachados.push(a), estadoRef: { current: { conversas: [{ id: 'c1', pedido }] } } })
  await acoes.gerarCobranca('c1', pedido, 'pix')
  await acoes.confirmarPagamento('c1', 50, 'pix')
  assert.equal(buscas.length, 0)
  assert.equal(despachados.length, 2)
  for (const aviso of despachados) assert.match(aviso.mensagem, /em_analise_de_fraude/)
})

console.log(`\n${passou} passaram, ${falhas.length} falharam.`)
if (falhas.length > 0) {
  console.log('Falhas:\n  - ' + falhas.join('\n  - '))
  process.exitCode = 1
}
