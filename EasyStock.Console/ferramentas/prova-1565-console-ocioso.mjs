/* eslint-disable no-console */
// Prova da #1565: o console parado não refaz trabalho.
//   1. a sincronização de 5 s com a mesma leitura devolve o MESMO estado (nada re-renderiza);
//   2. recarga silenciosa de lembretes e notificações não troca a situação;
//   3. recarga ao vivo: polling só com o SSE caído, eventos em rajada viram uma recarga;
//   4. GET sem resposta desiste sozinho;
//   5. SSE caído em seguida reconecta com espera crescente, com teto e variação (#2287).
//
//   node ferramentas/prova-1565-console-ocioso.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
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

const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const acao = await import('../src/aplicacao/acoes.js')
const { conversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

// --- 1. Sincronização sem mudança -------------------------------------------------------
const AGORA = Date.parse('2026-10-11T15:00:00Z')
const USUARIO = { id: 'u-1', nome: 'Thati' }
const resumo = (id, extra = {}) => ({
  id, canal: 'WhatsApp', contatoNome: `Cliente ${id}`, contatoIdExterno: `55119${id}`, clienteId: null,
  situacao: 'Aberta', assumidaPorUsuarioId: null, naoLidas: 0, dentroDaJanela: true,
  ultimaMensagemEm: '2026-10-11T14:50:00Z', ultimaMensagemTexto: 'oi', aguardaResposta: false,
  pedidoEmAndamentoId: null, ...extra,
})
const mensagem = (id, extra = {}) => ({
  id, direcao: 'Entrada', texto: `mensagem ${id}`, criadaEm: '2026-10-11T14:50:00Z', status: 'Recebida', ...extra,
})
const pedido = (extra = {}) => ({
  pedidoId: '11111111-2222-3333-4444-555555555555', status: 'em_preparo', itens: [], total: 50, leitura: 10, ...extra,
})
// Cada leitura monta objetos novos, como o laço faz a cada 5 s.
const leitura = ({ segunda = {}, mensagensDaPrimeira = [mensagem('m-1')], pedidoDaPrimeira = null } = {}) => [
  { ...conversaDaApi(resumo('c-1', pedidoDaPrimeira ? { pedidoEmAndamentoId: pedidoDaPrimeira.pedidoId } : {}), mensagensDaPrimeira, USUARIO, AGORA), pedido: pedidoDaApi(pedidoDaPrimeira) },
  { ...conversaDaApi(resumo('c-2', segunda), [mensagem('m-2')], USUARIO, AGORA), pedido: null },
]
const sincronizar = (estado, conversas) => reducer(estado, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas })
const partida = () => sincronizar({ ...estadoInicial, conversas: [], selecionadaId: null }, leitura())

await confere('1. a mesma leitura devolve o mesmo estado', () => {
  const primeiro = partida()
  const segundo = sincronizar(primeiro, leitura())
  assert.equal(segundo, primeiro, 'o estado trocou de identidade sem mudança nenhuma')
})

await confere('1. conversa que mudou troca; a que não mudou fica a mesma', () => {
  const primeiro = partida()
  const segundo = sincronizar(primeiro, leitura({ segunda: { naoLidas: 3 } }))
  assert.notEqual(segundo, primeiro)
  assert.equal(segundo.conversas[0], primeiro.conversas[0], 'a conversa parada trocou de identidade')
  assert.notEqual(segundo.conversas[1], primeiro.conversas[1])
  assert.equal(segundo.conversas[1].naoLidas, 3)
})

await confere('1. mensagem nova e mudança de status de mensagem chegam à tela', () => {
  const primeiro = partida()
  const comNova = sincronizar(primeiro, leitura({ mensagensDaPrimeira: [mensagem('m-1'), mensagem('m-9')] }))
  assert.equal(comNova.conversas[0].mensagens.length, 2)
  const falhou = sincronizar(primeiro, leitura({ mensagensDaPrimeira: [mensagem('m-1', { direcao: 'Saida', status: 'Falhou' })] }))
  assert.notEqual(falhou.conversas[0], primeiro.conversas[0])
})

await confere('1. pedido relido igual não troca a conversa; relido diferente troca', () => {
  const comPedido = sincronizar({ ...estadoInicial, conversas: [], selecionadaId: null }, leitura({ pedidoDaPrimeira: pedido() }))
  const relido = sincronizar(comPedido, leitura({ pedidoDaPrimeira: pedido({ leitura: 20 }) }))
  assert.equal(relido, comPedido, 'a releitura do mesmo pedido trocou o estado')
  const mudou = sincronizar(comPedido, leitura({ pedidoDaPrimeira: pedido({ leitura: 30, status: 'pronto' }) }))
  assert.equal(mudou.conversas[0].pedido.statusApi, 'pronto')
})

await confere('1. pedido confirmado por releitura nova não cede a uma leitura que saiu antes (#1548)', () => {
  const comPedido = sincronizar({ ...estadoInicial, conversas: [], selecionadaId: null }, leitura({ pedidoDaPrimeira: pedido({ leitura: 10 }) }))
  const confirmado = sincronizar(comPedido, leitura({ pedidoDaPrimeira: pedido({ leitura: 30 }) }))
  assert.equal(confirmado, comPedido)
  const atrasadaDaAcao = reducer(confirmado, { tipo: acao.SINCRONIZAR_PEDIDO, id: 'c-1', pedido: pedidoDaApi(pedido({ leitura: 20, status: 'pronto' })) })
  assert.equal(atrasadaDaAcao.conversas[0].pedido.statusApi, 'em_preparo', 'a leitura 20 saiu antes da 30 que confirmou a tela')
  const atrasadaDaPolling = sincronizar(confirmado, leitura({ pedidoDaPrimeira: pedido({ leitura: 20, status: 'pronto' }) }))
  assert.equal(atrasadaDaPolling.conversas[0].pedido.statusApi, 'em_preparo')
  const maisNova = reducer(confirmado, { tipo: acao.SINCRONIZAR_PEDIDO, id: 'c-1', pedido: pedidoDaApi(pedido({ leitura: 40, status: 'pronto' })) })
  assert.equal(maisNova.conversas[0].pedido.statusApi, 'pronto')
})

await confere('1. balão da dona ainda sem resposta da API continua depois de duas voltas', () => {
  const primeiro = partida()
  const enviando = {
    ...primeiro,
    conversas: primeiro.conversas.map((c) => c.id === 'c-1'
      ? { ...c, mensagens: [...c.mensagens, { id: 'local-1', dir: 'out', texto: 'já vai', status: 'enviando', em: '2026-10-11T14:59:00Z' }] }
      : c),
  }
  const volta1 = sincronizar(enviando, leitura())
  const volta2 = sincronizar(volta1, leitura())
  assert.ok(volta2.conversas[0].mensagens.some((m) => m.id === 'local-1'))
  assert.equal(volta2, volta1)
})

await confere('1. falha e volta da sincronização ainda trocam o estado', () => {
  const primeiro = partida()
  const caiu = reducer(primeiro, { tipo: acao.SINCRONIZACAO_FALHOU, mensagem: 'sem rede' })
  assert.equal(caiu.sincronizacao.estado, 'erro')
  const voltou = sincronizar(caiu, leitura())
  assert.equal(voltou.sincronizacao.estado, 'ok')
  assert.equal(voltou.conversas, primeiro.conversas, 'a lista não mudou, só a situação da sincronização')
})

await confere('1. conversa que saiu da lista sai do estado e a seleção some com ela (#2135: nenhuma, não a primeira do servidor)', () => {
  const primeiro = { ...partida(), selecionadaId: 'c-2' }
  const semSegunda = sincronizar(primeiro, leitura().slice(0, 1))
  assert.equal(semSegunda.conversas.length, 1)
  assert.equal(semSegunda.selecionadaId, null)
})

// --- 2. Consulta que se repete sozinha ---------------------------------------------------
const { aoComecarConsulta, aoConcluirConsulta, aoFalharConsulta } = await import('../src/aplicacao/situacaoDeConsulta.js')

await confere('2. recarga silenciosa sem novidade mantém a mesma situação', () => {
  const parada = { carregando: false, erro: null, total: 2, recentes: [{ id: 'n-1', lida: false }] }
  const comecou = aoComecarConsulta(parada, { silenciosa: true })
  assert.equal(comecou, parada, 'o relógio não liga "carregando"')
  const concluiu = aoConcluirConsulta(comecou, { total: 2, recentes: [{ id: 'n-1', lida: false }] })
  assert.equal(concluiu, parada, 'resposta igual não troca a situação')
})

await confere('2. novidade, erro e pedido de quem está na tela trocam a situação', () => {
  const parada = { carregando: false, erro: null, total: 2, recentes: [] }
  assert.equal(aoConcluirConsulta(parada, { total: 3, recentes: [] }).total, 3)
  assert.equal(aoComecarConsulta(parada).carregando, true, 'o botão Atualizar mostra carregando')
  const falhou = aoFalharConsulta(parada, 'Sem conexão com o EasyStok.')
  assert.equal(falhou.erro, 'Sem conexão com o EasyStok.')
  assert.equal(aoFalharConsulta(falhou, 'Sem conexão com o EasyStok.'), falhou, 'a mesma falha repetida não troca')
  assert.deepEqual(aoConcluirConsulta(falhou), parada, 'a volta limpa o erro')
  assert.deepEqual(aoConcluirConsulta({ carregando: true, erro: null }), { carregando: false, erro: null })
})

// --- 3. Recarga ao vivo --------------------------------------------------------------------
const { mock } = await import('node:test')
const { ligarRecargaAoVivo } = await import('../src/aplicacao/recargaAoVivo.js')

function montarAoVivo({ aleatorio } = {}) {
  mock.timers.enable({ apis: ['setTimeout', 'setInterval'] })
  let recargas = 0
  const conexoes = []
  const desligar = ligarRecargaAoVivo({
    aleatorio,
    recarregar: () => { recargas += 1 },
    recarregaCom: (evento) => evento.startsWith('pedido.'),
    conectar: (ouvinte) => {
      const conexao = { ...ouvinte, fechada: false }
      conexoes.push(conexao)
      return () => { conexao.fechada = true }
    },
  })
  return {
    desligar: () => { desligar(); mock.timers.reset() },
    andar: (ms) => mock.timers.tick(ms),
    recargas: () => recargas,
    conexao: () => conexoes.at(-1),
    conexoes,
  }
}

await confere('3. rajada de eventos de pedido vira uma recarga', () => {
  const t = montarAoVivo()
  try {
    t.conexao().aoEvento({ evento: 'ready' })
    t.andar(300)
    assert.equal(t.recargas(), 1, 'o ready relê a lista')
    for (let i = 0; i < 5; i += 1) t.conexao().aoEvento({ evento: 'pedido.mudou_status' })
    t.conexao().aoEvento({ evento: 'conversa.mensagem_recebida' })
    t.andar(300)
    assert.equal(t.recargas(), 2)
  } finally { t.desligar() }
})

await confere('3. SSE vivo: sem polling de 15 s, só a conferência de 30 s', () => {
  const t = montarAoVivo()
  try {
    t.conexao().aoEvento({ evento: 'ready' })
    t.andar(300)
    const base = t.recargas()
    t.andar(29000)
    assert.equal(t.recargas(), base, 'o intervalo de 15 s não recarrega com o SSE vivo')
    t.andar(1000)
    assert.equal(t.recargas(), base + 1)
  } finally { t.desligar() }
})

await confere('3. SSE caído: polling de 15 s e reconexão em 10 s', () => {
  const t = montarAoVivo()
  try {
    t.conexao().aoEvento({ evento: 'ready' })
    t.andar(300)
    const base = t.recargas()
    t.conexao().aoCair()
    t.andar(10000)
    assert.equal(t.conexoes.length, 2, 'reconectou')
    t.andar(20000)
    assert.equal(t.recargas(), base + 2, 'duas recargas em 30 s de SSE caído')
  } finally { t.desligar() }
})

await confere('3. antes do primeiro ready e depois de desligar', () => {
  const t = montarAoVivo()
  t.andar(15000)
  assert.equal(t.recargas(), 1, 'SSE que ainda não respondeu não deixa a tela parada')
  const conexao = t.conexao()
  t.desligar()
  assert.equal(conexao.fechada, true)
  conexao.aoCair()
  assert.equal(t.conexoes.length, 1, 'queda depois de desligar não reconecta')
})

// --- 5. Reconexão do SSE com espera crescente (#2287) ----------------------------------------
const { criarEsperaDeReconexao, RECONECTAR_TETO_MS } = await import('../src/aplicacao/recargaAoVivo.js')

await confere('5. a espera dobra a cada queda seguida, até o teto de 60 s', () => {
  assert.equal(RECONECTAR_TETO_MS, 60000)
  const espera = criarEsperaDeReconexao({ base: 10000, aleatorio: () => 0 })
  const esperas = Array.from({ length: 6 }, () => espera.proxima())
  assert.deepEqual(esperas, [10000, 20000, 40000, 60000, 60000, 60000])
})

await confere('5. a espera varia de 75% a 100%, para as abas não baterem juntas', () => {
  assert.equal(criarEsperaDeReconexao({ base: 10000, aleatorio: () => 1 }).proxima(), 7500)
  assert.equal(criarEsperaDeReconexao({ base: 10000, aleatorio: () => 0.5 }).proxima(), 8750)
  const noTeto = criarEsperaDeReconexao({ base: 5000, aleatorio: () => 1 })
  const ultima = Array.from({ length: 10 }, () => noTeto.proxima()).at(-1)
  assert.equal(ultima, 45000, 'no teto a variação também vale')
})

await confere('5. o ready zera: a queda depois de uma conexão boa volta à base', () => {
  const espera = criarEsperaDeReconexao({ base: 5000, aleatorio: () => 0 })
  espera.proxima(); espera.proxima(); espera.proxima()
  espera.zerar()
  assert.equal(espera.proxima(), 5000)
})

await confere('5. recarga ao vivo: quedas seguidas espaçam a reconexão; o ready volta à base', () => {
  const t = montarAoVivo({ aleatorio: () => 0 })
  try {
    t.conexao().aoCair()
    t.andar(10000)
    assert.equal(t.conexoes.length, 2, 'a primeira queda reconecta em 10 s')
    t.conexao().aoCair()
    t.andar(19999)
    assert.equal(t.conexoes.length, 2, 'a segunda queda seguida espera mais que 10 s')
    t.andar(1)
    assert.equal(t.conexoes.length, 3, 'e reconecta em 20 s')
    t.conexao().aoCair()
    t.andar(39999)
    assert.equal(t.conexoes.length, 3)
    t.andar(1)
    assert.equal(t.conexoes.length, 4, 'a terceira, em 40 s')
    t.conexao().aoEvento({ evento: 'ready' })
    t.conexao().aoCair()
    t.andar(10000)
    assert.equal(t.conexoes.length, 5, 'depois do ready a queda volta a reconectar em 10 s')
  } finally { t.desligar() }
})

await confere('5. todo ponto que abre o SSE da operação reconecta pela espera crescente', async () => {
  const { readdirSync, readFileSync } = await import('node:fs')
  const raiz = new URL('../src/', import.meta.url)
  const abremSse = readdirSync(raiz, { recursive: true })
    .filter((arquivo) => /\.jsx?$/.test(arquivo))
    .map((arquivo) => ({ arquivo, fonte: readFileSync(new URL(arquivo.replaceAll('\\', '/'), raiz), 'utf8') }))
    .filter(({ fonte }) => /conectar(EventosOperacao)?\(\{/.test(fonte) && /aoCair/.test(fonte))
    .filter(({ arquivo }) => !arquivo.replaceAll('\\', '/').endsWith('infra/api/eventosOperacao.js'))
  assert.ok(abremSse.length >= 5, `achou só ${abremSse.length} pontos que abrem o SSE`)
  const fixos = abremSse.filter(({ fonte }) => /setTimeout\(\s*(conectar|ouvir)\s*,\s*(RECONECTAR_MS|INTERVALO_MS|\d+)\s*\)/.test(fonte))
  assert.deepEqual(fixos.map(({ arquivo }) => arquivo), [], 'reconexão com espera fixa')
  const semEspera = abremSse.filter(({ fonte }) => !/criarEsperaDeReconexao\(/.test(fonte) || !/\.zerar\(\)/.test(fonte))
  assert.deepEqual(semEspera.map(({ arquivo }) => arquivo), [], 'ponto sem a espera crescente ou sem zerar no ready')
})

// --- 4. Leitura com prazo --------------------------------------------------------------------
const { chamarApi } = await import('../src/infra/api/cliente.js')

async function sinalDe(caminho, opcoes) {
  let visto
  globalThis.fetch = async (_url, { signal } = {}) => {
    visto = signal
    return new Response(JSON.stringify({ data: {} }), { status: 200 })
  }
  await chamarApi(caminho, { autenticado: false, ...opcoes })
  return visto
}

await confere('4. GET sai com prazo; escrita ganhou o seu (#2379); keepalive também (#2431); arquivo fica sem', async () => {
  assert.ok(await sinalDe('/api/atendimento/lembretes') instanceof AbortSignal, 'GET sem prazo')
  assert.ok(await sinalDe('/api/pedidos', { metodo: 'POST', corpo: {} }) instanceof AbortSignal, 'escrita sem prazo (#2379)')
  assert.ok(await sinalDe('/api/pedidos', { metodo: 'POST', corpo: {}, keepalive: true }) instanceof AbortSignal, 'keepalive sem prazo (#2431)')
  assert.equal(await sinalDe('/api/atendimento/conversas/c/mensagens/m/midia', { arquivo: true }), undefined)
  const proprio = AbortSignal.timeout(45000)
  assert.equal(await sinalDe('/api/atendimento/modelos', { sinal: proprio }), proprio, 'quem passa o seu prazo fica com ele')
})

// O prazo padrão é de 20 s; aqui ele já nasce estourado para a prova não esperar.
async function comPrazoEstourado(fetchFalso) {
  const original = AbortSignal.timeout
  AbortSignal.timeout = () => AbortSignal.abort(new DOMException('prazo', 'TimeoutError'))
  globalThis.fetch = fetchFalso
  try {
    return await chamarApi('/api/atendimento/lembretes', { autenticado: false }).catch((e) => e)
  } finally { AbortSignal.timeout = original }
}

await confere('4. prazo estourado é "demorou a responder", não a queda que liga o papel', async () => {
  const { quedaDaSincronizacao } = await import('../src/aplicacao/planoDeSincronizacao.js')
  const semChegar = await comPrazoEstourado(async (_url, { signal }) => { throw signal.reason })
  assert.equal(semChegar.codigo, 'SEM_RESPOSTA')
  assert.equal(quedaDaSincronizacao(semChegar), false)
  const noMeioDoCorpo = await comPrazoEstourado(async () => ({
    ok: true, status: 200, text: async () => { throw new DOMException('prazo', 'TimeoutError') },
  }))
  assert.equal(noMeioDoCorpo.codigo, 'SEM_RESPOSTA')
})

await confere('4. rede fora continua sendo queda de conexão', async () => {
  const { quedaDaSincronizacao } = await import('../src/aplicacao/planoDeSincronizacao.js')
  globalThis.fetch = async () => { throw new TypeError('Failed to fetch') }
  const erro = await chamarApi('/api/atendimento/lembretes', { autenticado: false }).catch((e) => e)
  assert.equal(erro.codigo, 'SEM_CONEXAO')
  assert.equal(quedaDaSincronizacao(erro), true)
  globalThis.fetch = async () => ({ ok: true, status: 200, text: async () => { throw new TypeError('network error') } })
  const cortada = await chamarApi('/api/atendimento/lembretes', { autenticado: false }).catch((e) => e)
  assert.equal(cortada.codigo, 'SEM_CONEXAO', 'conexão que cai no meio do corpo')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
