/* eslint-disable no-console */
// Prova da #1577: as telas do console gastam menos rede, memória e relógio.
//   1. ação de Entregas e da Cozinha não relê em dobro; o cadastro de entrega não volta a cada evento;
//   2. mídia da conversa: cache pequeno de endereços locais, liberados ao expulsar e ao sair;
//   3. SSE calado por mais de 2,5 heartbeats cai sozinho;
//   5. Pix sem contagem não tiquetaqueia; PDF aberto em aba nova tem o endereço liberado.
// Os hooks rodam de verdade num hospedeiro mínimo (o mesmo desenho da prova-ux-sistema); HTTP e SSE
// são trocados na fronteira e o tempo é o de `mock.timers`.
//
//   node ferramentas/prova-1577-telas-console.mjs

import { registerHooks } from 'node:module'
import { mock } from 'node:test'
import assert from 'node:assert/strict'

// --- Hospedeiro de hooks ---------------------------------------------------------------------
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
const pendente = () => {
  let resolver
  const promise = new Promise((res) => { resolver = res })
  return { promise, resolver }
}
globalThis.__prova1577 = {
  hooks,
  chamarApi(caminho, opcoes = {}) {
    const metodo = opcoes.metodo ?? 'GET'
    chamadas.push({ metodo, caminho })
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
registerHooks({
  resolve(especificador, contexto, proximo) {
    const pai = contexto.parentURL ?? ''
    if (especificador === 'react' && /\/(?:aplicacao\/use(?:EntregasApi|CozinhaApi|MidiaDaMensagem)|hooks\/useTiqueLocal)\.js$/.test(pai)) {
      return moduloDados('const h=globalThis.__prova1577.hooks; export const useState=h.useState,useRef=h.useRef,useCallback=h.useCallback,useEffect=h.useEffect;')
    }
    if (especificador === './cliente' && /\/infra\/api\/(?:entregasApi|kdsApi)\.js$/.test(pai)) {
      return moduloDados('export const chamarApi=(...a)=>globalThis.__prova1577.chamarApi(...a);')
    }
    if (especificador.endsWith('/conversasApi') && pai.endsWith('/aplicacao/useMidiaDaMensagem.js')) {
      return moduloDados('export const baixarMidia=(...a)=>globalThis.__prova1577.baixarMidia(...a);')
    }
    // Só as telas ouvem o SSE falso; a seção 3 importa o `eventosOperacao.js` de verdade.
    if (especificador.endsWith('/eventosOperacao') && /\/aplicacao\//.test(pai)) {
      return moduloDados('export const conectarEventosOperacao=(o)=>globalThis.__prova1577.conectar(o);')
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
const AGORA = Date.parse('2026-10-11T15:00:00Z')
const ligarRelogio = () => mock.timers.enable({ apis: ['setTimeout', 'setInterval', 'Date'], now: AGORA })
const andar = (ms) => mock.timers.tick(ms)

// --- 1. Recarga depois da ação -------------------------------------------------------------------
const CADASTRO = ['/api/atendimento/entregadores', '/api/minha-vitrine/entrega/janelas', '/api/minha-vitrine/entrega/bloqueios']
const leituras = () => chamadas.filter((c) => c.metodo === 'GET')
const doCadastro = () => leituras().filter((c) => CADASTRO.some((p) => c.caminho.startsWith(p)))
const daFila = () => leituras().filter((c) => c.caminho.startsWith('/api/kds/pedidos'))

// Monta a tela com o SSE já em `ready` e a primeira leitura feita; devolve com as chamadas zeradas.
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
const evento = (nome) => conexoes.at(-1).aoEvento({ evento: nome })

const { useEntregasApi } = await import('../src/aplicacao/useEntregasApi.js')
const { useCozinhaApi } = await import('../src/aplicacao/useCozinhaApi.js')

await confere('1. Entregas: montagem e `ready` leem tudo, cadastro incluído', async () => {
  ligarRelogio()
  const h = new Hospedeiro(useEntregasApi)
  try {
    await h.estabilizar()
    assert.equal(leituras().length, 7)
    assert.equal(doCadastro().length, 3)
    chamadas.length = 0
    evento('ready')
    andar(300)
    await h.estabilizar()
    assert.equal(h.resultado.aoVivo, true)
    assert.equal(leituras().length, 7, 'sem replay na API: o `ready` relê tudo')
  } finally { h.desmontar() }
})

await confere('1. Entregas: evento de pedido relê a operação, não o cadastro', async () => {
  // Duas leituras inteiras até aqui: a da montagem e a do `ready`.
  for (let i = 0; i < 2; i += 1) {
    responder('GET', '/api/atendimento/entregadores', () => [{ id: 'e-1', nome: 'Zé' }])
    responder('GET', '/api/minha-vitrine/entrega/janelas', () => [{ id: 'j-1' }])
  }
  const h = await montar(useEntregasApi)
  try {
    responder('GET', '/api/kds/pedidos?status=aguardando_aprovacao_baba', () => [{ id: 'p-9', status: 'pronto' }])
    for (let i = 0; i < 3; i += 1) evento('pedido.mudou_status')
    andar(300)
    await h.estabilizar()
    assert.equal(leituras().length, 4, 'rajada vira uma recarga, só da operação')
    assert.equal(doCadastro().length, 0, 'entregadores, janelas e bloqueios não mudam a cada pedido')
    assert.equal(h.resultado.pedidos[0].id, 'p-9', 'a operação nova chegou à tela')
    assert.equal(h.resultado.entregadores[0].id, 'e-1', 'o cadastro lido antes continua na tela')
    assert.equal(h.resultado.doDia.janelas[0].id, 'j-1')
  } finally { h.desmontar() }
})

await confere('1. Entregas: "Entregue" com o SSE vivo relê uma vez só, pelo evento', async () => {
  const h = await montar(useEntregasApi)
  try {
    const feito = h.resultado.acoes.entregue('v-1', 'p-1')
    await h.estabilizar()
    assert.equal(leituras().length, 0, 'a resposta da ação não relê: o evento dela vem aí')
    assert.equal(h.resultado.ocupado, true, 'os botões seguem travados até a tela refletir a ação')
    evento('pedido.mudou_status')
    andar(300)
    await h.estabilizar()
    assert.equal(await feito, true)
    assert.equal(h.resultado.ocupado, false)
    andar(5000)
    await h.estabilizar()
    assert.equal(leituras().length, 4, 'eram 14 GETs por toque')
  } finally { h.desmontar() }
})

await confere('1. Entregas: SSE vivo e mudo não deixa a tela velha (relê em 2 s)', async () => {
  const h = await montar(useEntregasApi)
  try {
    const feito = h.resultado.acoes.sair('v-1')
    await h.estabilizar()
    andar(1999)
    await h.estabilizar()
    assert.equal(leituras().length, 0)
    andar(1)
    await h.estabilizar()
    assert.equal(await feito, true)
    assert.equal(leituras().length, 4)
    assert.equal(h.resultado.ocupado, false)
  } finally { h.desmontar() }
})

await confere('1. Entregas: SSE caído, a ação relê na resposta como antes', async () => {
  const h = await montar(useEntregasApi, undefined, { aoVivo: false })
  try {
    assert.equal(await h.resultado.acoes.entregue('v-1', 'p-1'), true)
    assert.equal(leituras().length, 4)
    conexoes.at(-1).aoCair()
    chamadas.length = 0
    andar(15000)
    await h.estabilizar()
    assert.equal(leituras().length, 4, 'a polling de 15 s também relê só a operação')
  } finally { h.desmontar() }
})

await confere('1. Entregas: ação que a API não anuncia no SSE relê na resposta, sem esperar', async () => {
  const h = await montar(useEntregasApi)
  try {
    assert.equal(await h.resultado.acoes.incluirParada('v-1', 'p-1'), true)
    assert.equal(leituras().length, 4)
    assert.equal(doCadastro().length, 0)
  } finally { h.desmontar() }
})

await confere('1. Entregas: cadastro volta com ação de entregador, troca de dia e validade vencida', async () => {
  const h = await montar(useEntregasApi)
  try {
    assert.equal(await h.resultado.acoes.criarEntregador({ nome: 'Maria' }), true)
    assert.equal(doCadastro().length, 3, 'entregador novo aparece na lista')
    chamadas.length = 0
    h.resultado.mudarDia('2026-10-12')
    await h.estabilizar()
    assert.ok(leituras().some((c) => c.caminho === '/api/minha-vitrine/entrega/bloqueios?de=2026-10-12&ate=2026-10-12'))
    chamadas.length = 0
    evento('pedido.pago')
    andar(300)
    await h.estabilizar()
    assert.equal(doCadastro().length, 0)
    chamadas.length = 0
    andar(5 * 60000)
    evento('pedido.pago')
    andar(300)
    await h.estabilizar()
    assert.equal(doCadastro().length, 3, 'cadastro mexido em outro aparelho chega em até 5 min de operação')
  } finally { h.desmontar() }
})

await confere('1. Cozinha: um toque com o SSE vivo lê a fila uma vez, pelo evento', async () => {
  const h = await montar(useCozinhaApi, { data: null })
  try {
    const feito = h.resultado.avancar('p-1', 'preparando')
    await h.estabilizar()
    assert.equal(h.resultado.movendo.has('p-1'), true)
    evento('pedido.mudou_status')
    andar(300)
    await h.estabilizar()
    await feito
    await h.estabilizar()
    assert.equal(h.resultado.movendo.has('p-1'), false)
    andar(5000)
    await h.estabilizar()
    assert.equal(daFila().length, 1, 'eram duas leituras da fila por toque')
  } finally { h.desmontar() }
})

await confere('1. Cozinha: evento que chega antes da resposta lenta não prende o cartão por 2 s', async () => {
  const h = await montar(useCozinhaApi, { data: null })
  try {
    const lenta = pendente()
    responder('PATCH', '/api/kds/pedidos/p-1/status', () => lenta.promise)
    const feito = h.resultado.avancar('p-1', 'pronto')
    await h.estabilizar()
    evento('pedido.mudou_status')
    andar(300)
    await h.estabilizar()
    assert.equal(daFila().length, 1)
    lenta.resolver({})
    await feito
    await h.estabilizar()
    assert.equal(h.resultado.movendo.has('p-1'), false, 'a fila já foi relida pelo evento da própria ação')
    assert.equal(daFila().length, 1)
  } finally { h.desmontar() }
})

await confere('1. Cozinha: SSE mudo relê em 2 s; SSE caído relê na resposta', async () => {
  const vivo = await montar(useCozinhaApi, { data: null })
  try {
    const feito = vivo.resultado.avancar('p-1', 'pronto')
    await vivo.estabilizar()
    assert.equal(daFila().length, 0)
    andar(2000)
    await vivo.estabilizar()
    await feito
    assert.equal(daFila().length, 1)
  } finally { vivo.desmontar() }
  mock.timers.reset()
  chamadas.length = 0
  const caido = await montar(useCozinhaApi, { data: null }, { aoVivo: false })
  try {
    await caido.resultado.avancar('p-1', 'pronto')
    assert.equal(daFila().length, 1)
  } finally { caido.desmontar() }
})

await confere('1. Cozinha: trocar o dia no meio da ação solta o cartão', async () => {
  const h = await montar(useCozinhaApi, { data: null })
  try {
    const feito = h.resultado.avancar('p-1', 'pronto')
    await h.estabilizar()
    h.renderizar({ data: '2026-10-12' })
    await feito
    await h.estabilizar()
    assert.equal(h.resultado.movendo.has('p-1'), false)
  } finally { h.desmontar() }
})

await confere('1. recarga de ação: `ready` na rajada pede a leitura inteira; evento perto do prazo não dobra', async () => {
  const { criarRecargaDeAcao } = await import('../src/aplicacao/recargaDeAcao.js')
  ligarRelogio()
  const pedidas = []
  const recarga = criarRecargaDeAcao({ recarregar: (tudo) => { pedidas.push(tudo); return Promise.resolve() }, aoVivo: () => true })
  recarga.aoEvento(true)
  recarga.aoEvento(false)
  andar(300)
  assert.deepEqual(pedidas, [true])
  let solta = false
  recarga.depoisDaAcao(recarga.marcar()).then(() => { solta = true })
  andar(1900)
  recarga.aoEvento()
  andar(100)
  assert.deepEqual(pedidas, [true], 'o evento chegou: o prazo de 2 s não relê por conta própria')
  andar(200)
  await Promise.resolve(); await Promise.resolve(); await Promise.resolve()
  assert.deepEqual(pedidas, [true, false])
  assert.equal(solta, true)
})

// --- 2. Mídia da conversa ----------------------------------------------------------------------
await confere('2. cache de mídia: acerto vai para o fim da fila e o mais antigo sem uso é liberado', async () => {
  const { criarCacheDeMidia } = await import('../src/aplicacao/cacheDeMidia.js')
  const liberadas = []
  const cache = criarCacheDeMidia({ limite: 2, revogar: (url) => liberadas.push(url) })
  assert.equal(cache.usar('m-1'), null, 'ainda não baixou')
  for (const id of ['m-1', 'm-2']) { cache.guardar(id, `blob:${id}`); cache.soltar(id) }
  assert.equal(cache.usar('m-1'), 'blob:m-1')
  cache.soltar('m-1')
  cache.guardar('m-3', 'blob:m-3')
  cache.soltar('m-3')
  assert.deepEqual(liberadas, ['blob:m-2'], 'a m-1 foi usada por último e fica')
  assert.equal(cache.tamanho(), 2)
})

await confere('2. cache de mídia: endereço em uso por balão montado não é liberado', async () => {
  const { criarCacheDeMidia } = await import('../src/aplicacao/cacheDeMidia.js')
  const liberadas = []
  const cache = criarCacheDeMidia({ limite: 1, revogar: (url) => liberadas.push(url) })
  cache.guardar('audio', 'blob:audio')
  cache.guardar('foto', 'blob:foto')
  assert.deepEqual(liberadas, [], 'o áudio tocando e a foto na tela continuam válidos acima do limite')
  cache.soltar('foto')
  assert.deepEqual(liberadas, ['blob:foto'])
  assert.equal(cache.usar('audio'), 'blob:audio', 'segundo balão da mesma mensagem')
  cache.soltar('audio')
  assert.deepEqual(liberadas, ['blob:foto'], 'ainda há um balão segurando')
  cache.soltar('audio')
  assert.equal(cache.usar('audio'), 'blob:audio', 'dentro do limite, fica para a volta')
})

await confere('2. cache de mídia: download repetido libera o novo; limpar libera tudo', async () => {
  const { criarCacheDeMidia } = await import('../src/aplicacao/cacheDeMidia.js')
  const liberadas = []
  const cache = criarCacheDeMidia({ limite: 5, revogar: (url) => liberadas.push(url) })
  assert.equal(cache.guardar('m-1', 'blob:a'), 'blob:a')
  assert.equal(cache.guardar('m-1', 'blob:b'), 'blob:a', 'vale o que já estava na tela')
  assert.deepEqual(liberadas, ['blob:b'])
  cache.guardar('m-2', 'blob:c')
  cache.limpar()
  assert.deepEqual(liberadas.toSorted(), ['blob:a', 'blob:b', 'blob:c'])
  assert.equal(cache.tamanho(), 0)
  cache.soltar('m-1')
  assert.equal(cache.usar('m-1'), null)
})

// O hook de verdade: baixa só com o balão na tela e reaproveita ao voltar para a conversa.
const baixadas = []
const liberadasNoNavegador = []
globalThis.__prova1577.baixarMidia = (conversaId, mensagemId) => {
  baixadas.push(mensagemId)
  return Promise.resolve(new Blob([mensagemId]))
}
const revogarDoNavegador = URL.revokeObjectURL
URL.revokeObjectURL = (url) => { liberadasNoNavegador.push(url); revogarDoNavegador.call(URL, url) }
let sessaoDaProva = { expiraEm: Date.parse('2100-01-01'), empresa: { id: 'emp-1' }, usuario: { id: 'u-1' } }
globalThis.sessionStorage = { getItem: () => JSON.stringify(sessaoDaProva), removeItem() {}, setItem() {} }
const ouvintesDeSessao = []
globalThis.addEventListener = (tipo, ouvinte) => { if (tipo === 'easystok:sessao-alterada') ouvintesDeSessao.push(ouvinte) }
const { useMidiaDaMensagem } = await import('../src/aplicacao/useMidiaDaMensagem.js')
const balao = ({ midia, naTela }) => useMidiaDaMensagem(midia, naTela)
const MIDIA = { conversaId: 'c-1', mensagemId: 'm-1' }

await confere('2. mídia: balão fora da tela não baixa; ao entrar, baixa uma vez', async () => {
  baixadas.length = 0
  const h = new Hospedeiro(balao, { midia: MIDIA, naTela: false })
  try {
    await h.estabilizar()
    assert.deepEqual(baixadas, [], 'eram até 100 downloads ao abrir a conversa')
    assert.equal(h.resultado.url, null)
    h.renderizar({ midia: MIDIA, naTela: true })
    await h.estabilizar()
    assert.deepEqual(baixadas, ['m-1'])
    assert.match(h.resultado.url, /^blob:/)
  } finally { h.desmontar() }
})

await confere('2. mídia: voltar para a conversa reaproveita o arquivo, sem baixar nem liberar', async () => {
  baixadas.length = 0
  liberadasNoNavegador.length = 0
  const midia = { conversaId: 'c-1', mensagemId: 'm-2' }
  const ida = new Hospedeiro(balao, { midia, naTela: true })
  await ida.estabilizar()
  const url = ida.resultado.url
  ida.desmontar()
  const volta = new Hospedeiro(balao, { midia, naTela: true })
  try {
    await volta.estabilizar()
    assert.deepEqual(baixadas, ['m-2'], 'trocar de conversa e voltar baixava tudo de novo')
    assert.equal(volta.resultado.url, url)
    assert.deepEqual(liberadasNoNavegador, [])
  } finally { volta.desmontar() }
})

await confere('2. mídia: sair da sessão libera os arquivos; renovar o token não', async () => {
  liberadasNoNavegador.length = 0
  const h = new Hospedeiro(balao, { midia: { conversaId: 'c-1', mensagemId: 'm-3' }, naTela: true })
  await h.estabilizar()
  const url = h.resultado.url
  h.desmontar()
  assert.equal(ouvintesDeSessao.length, 1)
  ouvintesDeSessao[0]()
  assert.deepEqual(liberadasNoNavegador, [], 'mesma pessoa, mesma empresa: o áudio continua tocando')
  sessaoDaProva = null
  ouvintesDeSessao[0]()
  assert.ok(liberadasNoNavegador.includes(url), 'a foto de uma conta não fica na memória para a próxima')
})

// --- 3. SSE com vigia --------------------------------------------------------------------------
// A API manda `: heartbeat` a cada 25 s de fila parada (OperacaoEventosController.IntervaloHeartbeat).
await confere('3. vigia: 2,5 heartbeats de silêncio derrubam uma vez; qualquer sinal renova', async () => {
  const { criarVigiaDeSilencio, SILENCIO_MAXIMO_MS } = await import('../src/infra/api/vigiaDeSilencio.js')
  assert.equal(SILENCIO_MAXIMO_MS, 2.5 * 25000)
  ligarRelogio()
  let quedas = 0
  const vigia = criarVigiaDeSilencio({ aoCalar: () => { quedas += 1 } })
  vigia.sinal()
  for (let i = 0; i < 5; i += 1) { andar(25000); vigia.sinal() }
  assert.equal(quedas, 0, 'loja parada com heartbeat chegando não cai')
  andar(SILENCIO_MAXIMO_MS - 1)
  assert.equal(quedas, 0)
  andar(1)
  assert.equal(quedas, 1)
  andar(10 * SILENCIO_MAXIMO_MS)
  assert.equal(quedas, 1, 'avisa uma vez só')
  vigia.sinal()
  vigia.parar()
  andar(10 * SILENCIO_MAXIMO_MS)
  assert.equal(quedas, 1, 'parada, não avisa')
})

// O `conectarEventosOperacao` de verdade sobre um `fetch` em stream falso. O tempo é o de
// `mock.timers`; `respirar` deixa o stream (que não usa relógio) entregar o que tem.
const respirar = async () => { for (let i = 0; i < 5; i += 1) await new Promise((r) => setImmediate(r)) }
const emBytes = (texto) => new TextEncoder().encode(texto)
async function abrirSse() {
  ligarRelogio()
  const t = { eventos: [], quedas: 0, sinal: null, stream: null }
  globalThis.fetch = async (_url, { signal }) => {
    t.sinal = signal
    return new Response(new ReadableStream({ start(controlador) { t.stream = controlador } }), { status: 200 })
  }
  const { conectarEventosOperacao } = await import('../src/infra/api/eventosOperacao.js')
  t.fechar = conectarEventosOperacao({ aoEvento: (e) => t.eventos.push(e.evento), aoCair: () => { t.quedas += 1 } })
  await respirar()
  t.stream.enqueue(emBytes('event: ready\ndata: {}\n\n'))
  await respirar()
  assert.deepEqual(t.eventos, ['ready'])
  return t
}

await confere('3. SSE meio-aberto: sem nada por 62,5 s, a conexão é abortada e a tela é avisada', async () => {
  const t = await abrirSse()
  for (let i = 0; i < 4; i += 1) {
    andar(25000)
    t.stream.enqueue(emBytes(': heartbeat\n\n'))
    await respirar()
  }
  assert.equal(t.quedas, 0, 'o heartbeat, que não é evento, também conta como sinal de vida')
  andar(62500)
  await respirar()
  assert.equal(t.quedas, 1, 'antes ficava "Ao vivo" para sempre, sem atualizar')
  assert.equal(t.sinal.aborted, true)
  andar(200000)
  await respirar()
  assert.equal(t.quedas, 1, 'o aborto da vigia não avisa de novo')
})

await confere('3. SSE: fechar por fora e fim do stream desligam a vigia', async () => {
  const fechado = await abrirSse()
  fechado.fechar()
  andar(200000)
  await respirar()
  assert.equal(fechado.quedas, 0, 'quem fechou não recebe queda')
  mock.timers.reset()
  const encerrado = await abrirSse()
  encerrado.stream.close()
  await respirar()
  assert.equal(encerrado.quedas, 1, 'servidor fechou: queda normal')
  andar(200000)
  await respirar()
  assert.equal(encerrado.quedas, 1)
})

// --- 4. Animações que ficam ligadas o dia inteiro --------------------------------------------------
// Guarda de folha de estilo, não de pixel: a conferência visual é no navegador.
const { readFileSync } = await import('node:fs')
const folhaDe = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const quadrosDe = (folha, nome) => folha.match(new RegExp(`@keyframes ${nome}\\s*\\{((?:[^{}]*\\{[^{}]*\\})*)\\s*\\}`))?.[1] ?? null
const propriedadesDe = (quadros) => [...new Set([...quadros.matchAll(/([a-z-]+)\s*:/g)].map((m) => m[1]))].toSorted()
const regraDe = (folha, seletor) => folha.match(new RegExp(`(?:^|\\n)\\${seletor}\\s*\\{([^}]*)\\}`))?.[1] ?? null

await confere('4. "ao vivo" do KDS e pisca de SLA animam só opacity e transform', () => {
  const pulso = quadrosDe(folhaDe('features/cozinha/cozinha.module.css'), 'pulsarVivo')
  assert.deepEqual(propriedadesDe(pulso), ['opacity', 'transform'], 'box-shadow animado repinta a cada quadro')
  const pisca = quadrosDe(folhaDe('features/caixa-de-entrada/caixa.module.css'), 'piscaSla')
  assert.deepEqual(propriedadesDe(pisca), ['opacity'], 'background animado repinta a cada quadro')
})

await confere('4. topo e rodapé da moldura sem desfoque de fundo; o botão flutuante mantém', () => {
  const folha = folhaDe('app/moldura.module.css')
  assert.doesNotMatch(regraDe(folha, '.topo'), /backdrop-filter/)
  assert.doesNotMatch(regraDe(folha, '.rodape'), /backdrop-filter/)
  assert.match(regraDe(folha, '.topo'), /background:\s*var\(--vidro\)/, 'a cor do vidro fica')
  assert.match(regraDe(folha, '.botaoSimular'), /backdrop-filter/, 'esse flutua sobre conteúdo que rola')
})

// --- 5. Menores ----------------------------------------------------------------------------------
const { useTiqueLocal } = await import('../src/hooks/useTiqueLocal.js')
const relogioPix = ({ base, ancora, corre }) => useTiqueLocal(base, ancora, corre)
// Quantos segundos o relógio acordou a tela: cada tique que muda o número pede uma renderização.
function acordadas(h, segundos) {
  let vezes = 0
  for (let i = 0; i < segundos; i += 1) {
    andar(1000)
    if (h.sujo) { vezes += 1; h.renderizar() }
  }
  return vezes
}

await confere('5. Pix aguardando: o tique de 1 s desconta os segundos entre os tiques de 30 s', async () => {
  ligarRelogio()
  const h = new Hospedeiro(relogioPix, { base: 90000, ancora: 'a', corre: true })
  try {
    assert.equal(acordadas(h, 3), 3)
    assert.equal(h.resultado, 87000)
    h.renderizar({ base: 60000, ancora: 'b', corre: true })
    assert.equal(h.resultado, 60000, 'tique grande ou cobrança nova zera o tique local')
  } finally { h.desmontar() }
})

await confere('5. Pix pago, vencido ou cancelado: o relógio não acorda a tela a cada segundo', async () => {
  ligarRelogio()
  const h = new Hospedeiro(relogioPix, { base: 0, ancora: 'a', corre: false })
  try {
    assert.equal(acordadas(h, 60), 0, 'a ficha de um pedido pago re-renderizava 60 vezes por minuto')
    assert.equal(h.resultado, 0)
  } finally { h.desmontar() }
})

await confere('5. Pix que zerou entre dois tiques de 30 s para de contar', async () => {
  ligarRelogio()
  const h = new Hospedeiro(relogioPix, { base: 2500, ancora: 'a', corre: true })
  try {
    assert.equal(acordadas(h, 30), 3)
    assert.equal(h.resultado, 0)
  } finally { h.desmontar() }
})

// `imprimirPdf` de verdade, num navegador sem leitor de PDF (o Chrome do tablet Android).
const { imprimirPdf } = await import('../src/componentes/pdfNoNavegador.js')
async function semLeitorDePdf(fn) {
  ligarRelogio()
  liberadasNoNavegador.length = 0
  const abertas = []
  globalThis.window = { open: (url) => { abertas.push(url); return {} }, location: { assign() {} } }
  try { await fn(abertas) } finally { delete globalThis.window }
}

await confere('5. canhoto da Cozinha em aba nova: o endereço do PDF é liberado depois de 60 s', () => semLeitorDePdf((abertas) => {
  imprimirPdf({ bytes: new Uint8Array([37, 80, 68, 70]) })
  assert.equal(abertas.length, 1, 'o fluxo de impressão é o mesmo: o PDF abre em aba nova')
  andar(59999)
  assert.deepEqual(liberadasNoNavegador, [], 'a aba nova ainda pode estar carregando o arquivo')
  andar(1)
  assert.deepEqual(liberadasNoNavegador, abertas, 'cada canhoto impresso deixava um PDF preso na memória')
}))

await confere('5. com plano B (folha da tela), o endereço continua liberado na hora', () => semLeitorDePdf((abertas) => {
  let planos = 0
  imprimirPdf({ bytes: new Uint8Array([37, 80, 68, 70]) }, { planoB: () => { planos += 1 } })
  assert.equal(planos, 1)
  assert.equal(abertas.length, 0)
  assert.equal(liberadasNoNavegador.length, 1)
}))

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
