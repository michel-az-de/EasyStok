/* eslint-disable no-console */
// Prova da #1576: o laço de sincronização da inbox gasta menos a cada volta.
//   1. volta rápida lê só a página 1; a varredura completa fica na primeira volta e no ciclo lento;
//   2. pedido relido pelo status do resumo, não a cada mensagem da conversa;
//   3. no máximo 4 conversas lidas por vez;
//   4. mensagens e pedidos em cache sobrevivem à saída do Balcão;
//   5. leitura de mensagens que falha sem cache não esvazia a conversa;
//   6. #1966: `conversa.atualizada` lê a conversa na volta seguinte (nome novo sem esperar a varredura);
//   7. #1966: entrada nova na conversa aberta, com a aba visível, chama `marcar-lida` uma vez por lote.
//
//   node ferramentas/prova-1576-laco-sincronizacao.mjs

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
// Lidos a cada conferência: o que ainda não existe falha só a conferência que o usa.
const plano = () => import('../src/aplicacao/planoDeSincronizacao.js')
const ciclo = () => import('../src/aplicacao/cicloDeSincronizacao.js')

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

// --- Massa -------------------------------------------------------------------------------
const USUARIO = { id: 'u-1', nome: 'Thati' }
const BASE_MS = Date.parse('2026-09-30T14:50:00Z')
// `hora(0)` é a mais recente da massa; `hora(3)` é três minutos mais velha; negativo é mais nova.
const hora = (minutosAtras) => new Date(BASE_MS - minutosAtras * 60000).toISOString()
const resumo = (id, extra = {}) => ({
  id, canal: 'WhatsApp', contatoNome: `Cliente ${id}`, contatoIdExterno: `55119${id}`, clienteId: null,
  situacao: 'Automatica', assumidaPorUsuarioId: null, naoLidas: 0, dentroDaJanela: true,
  ultimaMensagemEm: hora(0), ultimaMensagemTexto: 'oi', aguardaResposta: false,
  pedidoEmAndamentoId: null, ...extra,
})
const mensagem = (id, extra = {}) => ({
  id, direcao: 'Entrada', autor: 'Cliente', tipoConteudo: 'Texto', texto: `mensagem ${id}`,
  enviadaEm: hora(0), status: 'Recebida', ...extra,
})
const PEDIDO = '11111111-2222-3333-4444-555555555555'
const pedido = (extra = {}) => ({ pedidoId: PEDIDO, status: 'em_preparo', itens: [], total: 50, frete: 0, ...extra })
const idDe = (i) => `c-${String(i + 1).padStart(3, '0')}`
// Do jeito que a API manda: a mais recente primeiro.
const inbox = (quantas, extra = () => ({})) =>
  Array.from({ length: quantas }, (_, i) => resumo(idDe(i), { ultimaMensagemEm: hora(i), ...extra(i) }))
const encerradas = (quantas) => inbox(quantas, () => ({ situacao: 'Encerrada' }))
const umaMensagemCada = (lista) => Object.fromEntries(lista.map((r) => [r.id, [mensagem(`m-${r.id}`)]]))

// EasyStok de mentira no `fetch`: lista paginada, mensagens e pedido da conversa.
const corpo = (data) => new Response(JSON.stringify({ data }), { status: 200 })
const umInstante = () => new Promise((resolver) => { setImmediate(resolver) })
function servidor({ conversas = [], mensagens = {}, pedidos = {} } = {}) {
  const s = { conversas, mensagens, pedidos, chamadas: [], falha: () => false, emVoo: 0, pico: 0 }
  globalThis.fetch = async (url) => {
    s.chamadas.push(url)
    if (s.falha(url)) throw new TypeError('Failed to fetch')
    const lista = /\/conversas\?limite=(\d+)&pagina=(\d+)/.exec(url)
    if (lista) {
      const [limite, pagina] = [Number(lista[1]), Number(lista[2])]
      return corpo(s.conversas.slice((pagina - 1) * limite, pagina * limite))
    }
    if (/\/conversas\/[^/]+\/marcar-lida$/.test(url)) return corpo(null)
    const doPedido = /\/conversas\/([^/]+)\/pedido/.exec(url)
    if (doPedido) return corpo(s.pedidos[doPedido[1]] ?? null)
    const daConversa = /\/conversas\/([^/]+)\/mensagens/.exec(url)
    if (daConversa) {
      s.emVoo += 1
      s.pico = Math.max(s.pico, s.emVoo)
      await umInstante()
      s.emVoo -= 1
      return corpo(s.mensagens[daConversa[1]] ?? [])
    }
    return new Response('{}', { status: 404 })
  }
  s.de = (trecho) => s.chamadas.filter((url) => url.includes(trecho))
  s.paginas = () => s.de('pagina=').map((url) => Number(/pagina=(\d+)/.exec(url)[1]))
  s.mensagensLidas = () => s.de('/mensagens').map((url) => /\/conversas\/([^/]+)\//.exec(url)[1]).sort()
  s.zerar = () => { s.chamadas.length = 0 }
  return s
}

// Uma "montagem" do Balcão: o estado nasce como no Provider do modo API (lista vazia) e o laço despacha nele.
const estadoVazio = () => ({
  ...estadoInicial({ conversas: [], catalogo: { janelas: [] }, regras: [], modoAgente: 'simulado' }),
  sincronizacao: { estado: 'carregando', mensagem: null, aviso: null },
})
async function montar({ memoria, dono = USUARIO.id, estado = estadoVazio(), selecionada = null, estaVivo } = {}) {
  const { criarCicloDeSincronizacao } = await ciclo()
  const m = { estado, acoes: [], selecionada, visivel: true }
  const volta = criarCicloDeSincronizacao({
    memoria, dono, estaVivo,
    despachar: (a) => { m.acoes.push(a); m.estado = reducer(m.estado, a) },
    lerSelecionada: () => m.selecionada,
    lerUsuario: () => USUARIO,
    lerAbaVisivel: () => m.visivel,
  })
  m.volta = volta
  m.rapida = () => volta({ cicloLento: false })
  m.lenta = () => volta({ cicloLento: true })
  m.ids = () => m.estado.conversas.map((c) => c.id)
  m.conversa = (id) => m.estado.conversas.find((c) => c.id === id)
  return m
}

// --- 1. Página 1 no ciclo rápido ---------------------------------------------------------
await confere('1. fundirPagina: a varredura completa substitui o mapa; a página 1 só atualiza quem veio', async () => {
  const { fundirPagina } = await plano()
  const [a, b, c] = [resumo('a', { ultimaMensagemEm: hora(1) }), resumo('b', { ultimaMensagemEm: hora(2) }), resumo('c', { ultimaMensagemEm: hora(3) })]
  const cheio = fundirPagina(new Map(), [a, b, c], { completa: true })
  assert.deepEqual([...cheio.keys()], ['a', 'b', 'c'])
  const bNovo = { ...b, naoLidas: 2 }
  const rapida = fundirPagina(cheio, [a, bNovo])
  assert.deepEqual([...rapida.keys()], ['a', 'b', 'c'], 'quem não veio na página 1 continua na lista')
  assert.equal(rapida.get('b'), bNovo)
  assert.equal(rapida.get('c'), c)
  assert.deepEqual([...fundirPagina(rapida, [a, bNovo], { completa: true }).keys()], ['a', 'b'], 'quem sumiu sai na varredura completa')
  assert.deepEqual([...cheio.keys()], ['a', 'b', 'c'], 'o mapa de entrada não é alterado')
})

await confere('1. fundirPagina: mais recente primeiro; conversa nova e a que recebeu mensagem sobem', async () => {
  const { fundirPagina } = await plano()
  const [a, b, c] = [resumo('a', { ultimaMensagemEm: hora(1) }), resumo('b', { ultimaMensagemEm: hora(2) }), resumo('c', { ultimaMensagemEm: hora(3) })]
  const cheio = fundirPagina(new Map(), [a, b, c], { completa: true })
  const cComMensagem = { ...c, ultimaMensagemEm: hora(0) }
  const nova = resumo('d', { ultimaMensagemEm: hora(-1) })
  assert.deepEqual([...fundirPagina(cheio, [nova, cComMensagem, a]).keys()], ['d', 'c', 'a', 'b'])
  // A API manda a data sem fuso e com fração de tamanho variável.
  const semFuso = [
    resumo('x', { ultimaMensagemEm: '2026-09-30T14:50:00' }),
    resumo('y', { ultimaMensagemEm: '2026-09-30T14:50:00.25' }),
    resumo('z', { ultimaMensagemEm: '2026-09-30T14:50:00.1234567' }),
    resumo('w', { ultimaMensagemEm: null }),
  ]
  assert.deepEqual([...fundirPagina(new Map(), semFuso, { completa: true }).keys()], ['y', 'z', 'x', 'w'])
})

await confere('1. fundirPagina: empate em `ultimaMensagemEm` fica na ordem em que a API mandou primeiro', async () => {
  const { fundirPagina } = await plano()
  const [x, y, z] = ['x', 'y', 'z'].map((id) => resumo(id))
  const primeira = fundirPagina(new Map(), [x, y, z], { completa: true })
  assert.deepEqual([...primeira.keys()], ['x', 'y', 'z'])
  // A API ordena só por `ultimaMensagemEm`: o empate pode vir trocado na volta seguinte.
  assert.deepEqual([...fundirPagina(primeira, [z, y, x]).keys()], ['x', 'y', 'z'], 'a lista pulou na volta rápida')
  assert.deepEqual([...fundirPagina(primeira, [y, z, x], { completa: true }).keys()], ['x', 'y', 'z'], 'a lista pulou na varredura')
})

await confere('1. a primeira volta varre a inbox inteira; a rápida lê só a página 1; o ciclo lento varre de novo', async () => {
  const s = servidor({ conversas: encerradas(150) })
  const m = await montar()
  await m.rapida()
  assert.deepEqual(s.paginas(), [1, 2], 'a primeira volta é completa mesmo fora do ciclo lento')
  assert.equal(m.estado.conversas.length, 150)
  s.zerar()
  await m.rapida()
  assert.deepEqual(s.paginas(), [1], 'a volta rápida seguiu as páginas')
  assert.equal(m.estado.conversas.length, 150, 'a lista despachada continua inteira')
  s.zerar()
  await m.lenta()
  assert.deepEqual(s.paginas(), [1, 2])
})

await confere('1. duas voltas iguais devolvem o MESMO estado, com a conversa aberta fora da página 1', async () => {
  servidor({ conversas: encerradas(150) })
  const m = await montar()
  await m.lenta()
  m.estado = { ...m.estado, selecionadaId: idDe(119) }
  m.selecionada = idDe(119)
  const antes = m.estado
  await m.rapida()
  assert.equal(m.estado.selecionadaId, idDe(119), 'a conversa aberta foi trocada')
  assert.equal(m.estado, antes, 'o estado trocou de identidade sem mudança nenhuma')
  await m.lenta()
  assert.equal(m.estado, antes)
})

await confere('1. empate que a API devolve em outra ordem não troca o estado', async () => {
  const empatadas = ['a', 'b', 'c'].map((id) => resumo(id, { situacao: 'Encerrada' }))
  const s = servidor({ conversas: empatadas })
  const m = await montar()
  await m.lenta()
  const antes = m.estado
  s.conversas = [...empatadas].reverse()
  await m.rapida()
  assert.deepEqual(m.ids(), ['a', 'b', 'c'])
  assert.equal(m.estado, antes)
  await m.lenta()
  assert.equal(m.estado, antes)
})

await confere('1. conversa que sumiu fica na volta rápida e sai na varredura completa', async () => {
  const s = servidor({ conversas: encerradas(150) })
  const m = await montar()
  await m.lenta()
  s.conversas = s.conversas.filter((r) => r.id !== idDe(129))
  await m.rapida()
  assert.equal(m.estado.conversas.length, 150)
  await m.lenta()
  assert.equal(m.estado.conversas.length, 149)
  assert.equal(m.conversa(idDe(129)), undefined)
})

await confere('1. página 1 incompleta já é a inbox inteira: quem sumiu sai na volta rápida', async () => {
  const s = servidor({ conversas: encerradas(3) })
  const m = await montar()
  await m.lenta()
  s.conversas = s.conversas.slice(0, 2)
  await m.rapida()
  assert.deepEqual(m.ids(), [idDe(0), idDe(1)])
})

await confere('1. conversa nova e mensagem nova da página 1 chegam na volta rápida', async () => {
  const s = servidor({ conversas: encerradas(150) })
  const m = await montar()
  await m.lenta()
  const nova = resumo('nova', { ultimaMensagemEm: hora(-2) })
  const reaberta = { ...s.conversas[140], situacao: 'Automatica', ultimaMensagemEm: hora(-1) }
  s.conversas = [nova, reaberta, ...s.conversas.filter((r) => r.id !== reaberta.id)]
  s.mensagens = { nova: [mensagem('m-nova')], [reaberta.id]: [mensagem('m-reaberta')] }
  s.zerar()
  await m.rapida()
  assert.deepEqual(s.paginas(), [1])
  assert.deepEqual(m.ids().slice(0, 3), ['nova', reaberta.id, idDe(0)])
  assert.equal(m.estado.conversas.length, 151)
  assert.equal(m.conversa('nova').mensagens.length, 1)
  assert.equal(m.conversa(reaberta.id).mensagens.length, 1)
})

await confere('1. conversa aberta fora da página 1: a volta rápida lê até a página dela e traz o resumo de agora', async () => {
  const s = servidor({ conversas: encerradas(250) })
  const m = await montar()
  await m.lenta()
  m.selecionada = idDe(119)
  s.zerar()
  await m.rapida()
  assert.deepEqual(s.paginas(), [1, 2], 'a conversa aberta ficou com o resumo da última varredura')
  // A dona reabriu a conversa antiga: a situação mudou no servidor sem mensagem nova.
  s.conversas = s.conversas.map((r) => (r.id === idDe(119) ? { ...r, situacao: 'Assumida', assumidaPorUsuarioId: USUARIO.id } : r))
  await m.rapida()
  assert.equal(m.conversa(idDe(119)).situacaoApi, 'Assumida', 'o resumo velho desfez a ação na conversa aberta')
  assert.equal(m.estado.conversas.length, 250)
})

await confere('1. conversa que acabou de ser fechada na tela segue lida de fresco por um minuto', async () => {
  const s = servidor({ conversas: encerradas(250) })
  const m = await montar()
  await m.lenta()
  m.selecionada = idDe(119)
  await m.rapida()
  // Agiu na conversa antiga e clicou em outra antes da volta seguinte.
  m.selecionada = idDe(0)
  s.conversas = s.conversas.map((r) => (r.id === idDe(119) ? { ...r, situacao: 'Assumida', assumidaPorUsuarioId: USUARIO.id } : r))
  s.zerar()
  await m.rapida()
  assert.deepEqual(s.paginas(), [1, 2])
  assert.equal(m.conversa(idDe(119)).situacaoApi, 'Assumida', 'o resumo de antes da ação voltou para a tela')
  for (let i = 0; i < 12; i += 1) await m.rapida()
  s.zerar()
  await m.rapida()
  assert.deepEqual(s.paginas(), [1], 'depois de um minuto a volta rápida volta a ler só a página 1')
})

await confere('1. conversa aberta que não está na inbox não faz a volta rápida seguir as páginas', async () => {
  const s = servidor({ conversas: encerradas(250) })
  const m = await montar({ selecionada: 'nao-existe' })
  await m.lenta()
  s.zerar()
  await m.rapida()
  assert.deepEqual(s.paginas(), [1])
})

await confere('1. falha da lista avisa a tela e a volta seguinte refaz a varredura completa', async () => {
  const s = servidor({ conversas: encerradas(150) })
  const m = await montar()
  s.falha = (url) => url.includes('pagina=2')
  await m.lenta()
  assert.equal(m.estado.sincronizacao.estado, 'erro')
  assert.ok(m.acoes.some((a) => a.tipo === acao.CONEXAO_CAIU), 'rede fora não ligou a faixa do papel')
  s.falha = () => false
  s.zerar()
  await m.rapida()
  assert.deepEqual(s.paginas(), [1, 2], 'depois da varredura que falhou a volta seguinte ainda é completa')
  assert.equal(m.estado.sincronizacao.estado, 'ok')
  assert.equal(m.estado.conversas.length, 150)
})

// --- 2. Pedido relido por status ---------------------------------------------------------
const comPedido = (extra = {}) => resumo('c-1', { pedidoEmAndamentoId: PEDIDO, ultimaMensagemEm: hora(5), ...extra })
const pedidoGuardado = (status = 'em_preparo') => ({ pedidoId: PEDIDO, ultima: hora(5), dados: { status } })

await confere('2. com `pedidoStatus` no resumo, mensagem nova não relê o pedido; status diferente relê', async () => {
  const { deveRelerPedido } = await plano()
  const comMensagemNova = comPedido({ ultimaMensagemEm: hora(0), pedidoStatus: 'em_preparo' })
  assert.equal(deveRelerPedido(comMensagemNova, pedidoGuardado()), false, 'releu o pedido só porque chegou mensagem')
  assert.equal(deveRelerPedido(comPedido({ pedidoStatus: 'pronto' }), pedidoGuardado()), true)
  assert.equal(deveRelerPedido(comPedido({ pedidoStatus: 'cancelado' }), pedidoGuardado('entregue')), true,
    'status final no cache não segura o status novo do resumo')
})

await confere('2. com `pedidoStatus`: selecionada, pedido novo e ciclo lento seguem relendo; final igual fica no cache', async () => {
  const { deveRelerPedido } = await plano()
  const igual = comPedido({ pedidoStatus: 'em_preparo' })
  assert.equal(deveRelerPedido(igual, pedidoGuardado()), false)
  assert.equal(deveRelerPedido(igual, pedidoGuardado(), { selecionadaId: 'c-1' }), true)
  assert.equal(deveRelerPedido(igual, pedidoGuardado(), { cicloLento: true }), true)
  assert.equal(deveRelerPedido(igual, undefined), true)
  assert.equal(deveRelerPedido(igual, { ...pedidoGuardado(), pedidoId: 'outro' }), true)
  const entregue = comPedido({ pedidoStatus: 'entregue' })
  assert.equal(deveRelerPedido(entregue, pedidoGuardado('entregue'), { selecionadaId: 'c-1', cicloLento: true }), false)
  assert.equal(deveRelerPedido(resumo('c-1', { pedidoStatus: 'em_preparo' }), pedidoGuardado()), false, 'sem pedido não há o que reler')
})

await confere('2. status do resumo em formato diferente do pedido lido relê uma vez, não a cada volta', async () => {
  const { deveRelerPedido } = await plano()
  const resumoDivergente = comPedido({ pedidoStatus: 'EmPreparo' })
  assert.equal(deveRelerPedido(resumoDivergente, pedidoGuardado()), true, 'a primeira diferença relê')
  const relido = { ...pedidoGuardado(), statusDoResumo: 'EmPreparo' }
  assert.equal(deveRelerPedido(resumoDivergente, relido), false, 'releu de novo o pedido que acabou de ler')
  assert.equal(deveRelerPedido(comPedido({ pedidoStatus: 'Pronto' }), relido), true, 'mudança de status segue relendo')
})

await confere('2. sem `pedidoStatus` (API antiga) o pedido é relido como antes', async () => {
  const { deveRelerPedido } = await plano()
  for (const antigo of [comPedido(), comPedido({ pedidoStatus: null })]) {
    assert.equal(deveRelerPedido(antigo, pedidoGuardado()), false)
    assert.equal(deveRelerPedido({ ...antigo, ultimaMensagemEm: hora(0) }, pedidoGuardado()), true, 'conversa que mudou relê')
    assert.equal(deveRelerPedido(antigo, pedidoGuardado(), { cicloLento: true }), true)
    assert.equal(deveRelerPedido(antigo, pedidoGuardado(), { selecionadaId: 'c-1' }), true)
    assert.equal(deveRelerPedido({ ...antigo, ultimaMensagemEm: hora(0) }, pedidoGuardado('entregue'), { cicloLento: true }), false)
    assert.equal(deveRelerPedido(antigo, undefined), true)
  }
})

await confere('2. no laço: mensagem nova com o mesmo status não busca o pedido; status novo busca', async () => {
  const s = servidor({
    conversas: [comPedido({ pedidoStatus: 'em_preparo' })],
    mensagens: { 'c-1': [mensagem('m-1')] },
    pedidos: { 'c-1': pedido() },
  })
  const m = await montar()
  await m.lenta()
  assert.equal(s.de('/pedido').length, 1)
  assert.equal(m.conversa('c-1').pedido.statusApi, 'em_preparo')
  s.conversas = [comPedido({ pedidoStatus: 'em_preparo', ultimaMensagemEm: hora(0) })]
  s.mensagens = { 'c-1': [mensagem('m-1'), mensagem('m-2')] }
  s.zerar()
  await m.rapida()
  assert.equal(m.conversa('c-1').mensagens.length, 2)
  assert.equal(s.de('/pedido').length, 0, 'a mensagem nova releu o pedido')
  s.conversas = [comPedido({ pedidoStatus: 'pronto', ultimaMensagemEm: hora(0) })]
  s.pedidos = { 'c-1': pedido({ status: 'pronto' }) }
  await m.rapida()
  assert.equal(s.de('/pedido').length, 1)
  assert.equal(m.conversa('c-1').pedido.statusApi, 'pronto')
  await m.rapida()
  assert.equal(s.de('/pedido').length, 1, 'status igual ao do cache não relê')
})

// --- 3. Fila de 4 ------------------------------------------------------------------------
const espera = (ms) => new Promise((resolver) => { setTimeout(resolver, ms) })

await confere('3. emFila: no máximo 4 por vez e o resultado na ordem de entrada', async () => {
  const { emFila } = await plano()
  let emVoo = 0
  let pico = 0
  const itens = Array.from({ length: 20 }, (_, i) => i)
  const resultado = await emFila(itens, 4, async (n) => {
    emVoo += 1
    pico = Math.max(pico, emVoo)
    await espera((20 - n) % 5)
    emVoo -= 1
    return n * 2
  })
  assert.equal(pico, 4)
  assert.deepEqual(resultado, itens.map((n) => n * 2))
})

await confere('3. emFila: lista vazia, lista menor que o limite e função que não é assíncrona', async () => {
  const { emFila } = await plano()
  assert.deepEqual(await emFila([], 4, async () => 1), [])
  assert.deepEqual(await emFila(['a', 'b'], 4, (letra, i) => `${letra}${i}`), ['a0', 'b1'])
})

await confere('3. emFila: a rejeição propaga e a fila para de puxar', async () => {
  const { emFila } = await plano()
  const iniciados = []
  const itens = Array.from({ length: 12 }, (_, i) => i)
  await assert.rejects(emFila(itens, 2, async (n) => {
    iniciados.push(n)
    await espera(1)
    if (n === 1) throw new Error('quebrou')
    return n
  }), /quebrou/)
  await espera(20)
  assert.ok(iniciados.length <= 4, `seguiu puxando depois da falha: ${iniciados.length} de 12`)
})

await confere('3. no laço: 20 conversas, no máximo 4 leituras de mensagens em andamento', async () => {
  const conversas = inbox(20)
  const s = servidor({ conversas, mensagens: umaMensagemCada(conversas) })
  const m = await montar()
  await m.lenta()
  assert.equal(s.de('/mensagens').length, 20)
  assert.equal(s.pico, 4)
  assert.deepEqual(m.ids(), conversas.map((r) => r.id))
  assert.ok(m.estado.conversas.every((c) => c.mensagens.length === 1))
})

// --- 4. Caches que sobrevivem ao Balcão --------------------------------------------------
const balcao = () => {
  const conversas = inbox(5)
  conversas[0] = { ...conversas[0], pedidoEmAndamentoId: PEDIDO }
  conversas[1] = { ...conversas[1], pedidoEmAndamentoId: 'aaaaaaaa-2222-3333-4444-555555555555' }
  return servidor({
    conversas,
    mensagens: umaMensagemCada(conversas),
    pedidos: {
      [idDe(0)]: pedido({ status: 'entregue' }),
      [idDe(1)]: pedido({ pedidoId: 'aaaaaaaa-2222-3333-4444-555555555555' }),
    },
  })
}

await confere('4. voltar ao Balcão com a mesma memória não relê as mensagens de quem não mudou', async () => {
  const { criarMemoriaDoLaco } = await ciclo()
  const memoria = criarMemoriaDoLaco()
  const s = balcao()
  const antes = await montar({ memoria })
  await antes.lenta()
  assert.equal(s.de('/mensagens').length, 5)
  assert.equal(s.de('/pedido').length, 2)

  // Foi à Cozinha e voltou: Provider novo (estado vazio), mesma memória. Enquanto isso c-004 recebeu mensagem.
  const mudou = { ...s.conversas[3], ultimaMensagemEm: hora(-1) }
  s.conversas = [mudou, ...s.conversas.filter((r) => r.id !== mudou.id)]
  s.mensagens = { ...s.mensagens, [mudou.id]: [mensagem('m-velha', { enviadaEm: hora(3) }), mensagem('m-nova', { enviadaEm: hora(-1) })] }
  s.zerar()
  const depois = await montar({ memoria, selecionada: idDe(2) })
  await depois.lenta()
  assert.deepEqual(s.paginas(), [1], 'a volta ao Balcão ainda lê a lista')
  assert.deepEqual(s.mensagensLidas(), [idDe(2), idDe(3)], 'só a conversa aberta e a que mudou são relidas')
  assert.deepEqual(s.de('/pedido').length, 1, 'pedido entregue fica no cache; o em preparo é conferido na volta')
  assert.equal(depois.estado.conversas.length, 5)
  assert.ok(depois.estado.conversas.every((c) => c.mensagens.length >= 1), 'conversa sem mensagens depois da volta')
  assert.equal(depois.conversa(mudou.id).mensagens.length, 2)
  assert.equal(depois.conversa(idDe(0)).pedido.statusApi, 'entregue')
  assert.equal(depois.conversa(idDe(1)).pedido.statusApi, 'em_preparo')
})

await confere('4. sem memória de fora o laço cria a sua e outra montagem relê tudo', async () => {
  const s = balcao()
  await (await montar()).lenta()
  s.zerar()
  await (await montar()).lenta()
  assert.equal(s.de('/mensagens').length, 5)
  assert.equal(s.de('/pedido').length, 2)
})

await confere('4. outro usuário na mesma memória começa do zero', async () => {
  const { criarMemoriaDoLaco } = await ciclo()
  const memoria = criarMemoriaDoLaco()
  const s = balcao()
  await (await montar({ memoria, dono: 'u-1' })).lenta()
  s.zerar()
  await (await montar({ memoria, dono: 'u-2' })).lenta()
  assert.equal(s.de('/mensagens').length, 5, 'o cache de outro usuário foi aproveitado')
  assert.equal(s.de('/pedido').length, 2)
  s.zerar()
  await (await montar({ memoria, dono: 'u-2' })).lenta()
  assert.equal(s.de('/mensagens').length, 0, 'o mesmo usuário perdeu o cache')
})

await confere('4. conversa que saiu da inbox sai também dos caches', async () => {
  const { criarMemoriaDoLaco } = await ciclo()
  const memoria = criarMemoriaDoLaco()
  const s = balcao()
  const m = await montar({ memoria })
  await m.lenta()
  assert.equal(memoria.mensagens.size, 5)
  s.conversas = s.conversas.slice(2)
  await m.lenta()
  assert.deepEqual([...memoria.mensagens.keys()].sort(), [idDe(2), idDe(3), idDe(4)])
  assert.equal(memoria.pedidos.size, 0)
})

await confere('4. montagem que saiu da tela não despacha nem segue lendo', async () => {
  const conversas = inbox(5)
  const s = servidor({ conversas, mensagens: umaMensagemCada(conversas) })
  const m = await montar({ estaVivo: () => false })
  await m.lenta()
  assert.equal(m.acoes.length, 0)
  assert.equal(s.de('/mensagens').length, 0)
})

await confere('4. a memória nasce onde sobrevive ao Balcão, troca com a sessão e chega ao laço', () => {
  const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
  const app = fonte('app/App.jsx')
  const comAcesso = app.slice(app.indexOf('function AppComAcesso('))
  assert.match(comAcesso, /useState\(criarMemoriaDoLaco\)/, 'a memória não nasce no AppComAcesso')
  assert.match(app, /<AppComAcesso key=\{identidadeDaSessao\(sessao\)\}/, 'o AppComAcesso não troca com a sessão')
  assert.match(app, /<AtendimentoProvider [^>]*memoriaDoLaco=\{memoriaDoLaco\}/)
  assert.match(fonte('aplicacao/AtendimentoProvider.jsx'), /useSincronizacaoApi\(\{[^}]*memoria: memoriaDoLaco/)
  const hook = fonte('aplicacao/useSincronizacaoApi.js')
  assert.match(hook, /memoria: memoria \?\? criarMemoriaDoLaco\(\)/)
  assert.match(hook, /dono: usuarioId/)
})

// --- 5. Erro sem cache não apaga a tela --------------------------------------------------
const sincronizar = (estado, conversas, semMensagens) =>
  reducer(estado, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas, ...(semMensagens ? { semMensagens } : {}) })
// Cada leitura monta objetos novos, como o laço faz a cada 5 s.
const lida = (id, mensagens, extra = {}) => ({ ...conversaDaApi(resumo(id, extra), mensagens, USUARIO, BASE_MS), pedido: null })
const duasMensagens = () => [mensagem('m-1', { enviadaEm: hora(9) }), mensagem('m-2', { enviadaEm: hora(8) })]
const naTela = () => sincronizar(estadoVazio(), [lida('c-1', duasMensagens()), lida('c-2', [mensagem('m-3')])])

await confere('5. reducer: leitura que falhou sem cache mantém as mensagens da tela e devolve o mesmo estado', () => {
  const antes = naTela()
  const depois = sincronizar(antes, [lida('c-1', []), lida('c-2', [mensagem('m-3')])], ['c-1'])
  assert.equal(depois.conversas[0].mensagens.length, 2, 'a conversa ficou vazia por um ciclo')
  assert.equal(depois, antes, 'a volta que não leu trocou o estado')
  assert.equal(depois.conversas[0].janelaExpiraEm, antes.conversas[0].janelaExpiraEm)
})

await confere('5. reducer: a marca não fica no estado e o resto da conversa ainda atualiza', () => {
  const antes = naTela()
  const depois = sincronizar(antes, [lida('c-1', [], { naoLidas: 3 }), lida('c-2', [mensagem('m-3')])], ['c-1'])
  assert.equal(depois.conversas[0].naoLidas, 3)
  assert.equal(depois.conversas[0].mensagens.length, 2)
  assert.equal(depois.conversas[1], antes.conversas[1])
  assert.equal(Object.hasOwn(depois, 'semMensagens'), false)
  assert.deepEqual(Object.keys(depois.conversas[0]).sort(), Object.keys(antes.conversas[0]).sort())
  assert.equal(sincronizar(depois, [lida('c-1', duasMensagens(), { naoLidas: 3 }), lida('c-2', [mensagem('m-3')])]), depois,
    'a leitura seguinte, igual à tela, trocou o estado')
})

await confere('5. reducer: conversa nova sem leitura nasce vazia; lista vazia lida de verdade esvazia', () => {
  const antes = naTela()
  const comNova = sincronizar(antes, [lida('c-1', duasMensagens()), lida('c-2', [mensagem('m-3')]), lida('c-9', [])], ['c-9'])
  assert.deepEqual(comNova.conversas[2].mensagens, [])
  const esvaziou = sincronizar(antes, [lida('c-1', []), lida('c-2', [mensagem('m-3')])])
  assert.deepEqual(esvaziou.conversas[0].mensagens, [], 'sem a marca, lista vazia é o que o servidor tem')
})

await confere('5. no laço: mensagens que falham sem cache não apagam a conversa; com cache vale o cache', async () => {
  const conversas = inbox(2)
  const s = servidor({ conversas, mensagens: umaMensagemCada(conversas) })
  const comCache = await montar({ selecionada: idDe(0) })
  await comCache.lenta()
  assert.equal(comCache.conversa(idDe(0)).mensagens.length, 1)
  // A tela segue de pé e o cache do laço se perdeu (laço recomeçado).
  const semCache = await montar({ estado: comCache.estado, selecionada: idDe(0) })
  s.falha = (url) => url.includes('/mensagens')
  const antes = semCache.estado
  await semCache.lenta()
  assert.equal(semCache.conversa(idDe(0)).mensagens.length, 1, 'a conversa aberta ficou vazia')
  assert.equal(semCache.conversa(idDe(1)).mensagens.length, 1)
  assert.equal(semCache.estado, antes)
  assert.equal(semCache.estado.sincronizacao.estado, 'ok')
  await comCache.rapida()
  assert.equal(comCache.conversa(idDe(0)).mensagens.length, 1)
  // A leitura volta e a conversa é lida de novo.
  s.falha = () => false
  s.mensagens = { ...s.mensagens, [idDe(0)]: [mensagem(`m-${idDe(0)}`), mensagem('m-outra')] }
  await semCache.rapida()
  assert.equal(semCache.conversa(idDe(0)).mensagens.length, 2)
})

// --- 6. #1966: `conversa.atualizada` troca o nome na inbox sem esperar a varredura --------------
await confere('6. evento: `conversa.atualizada` com conversaId é da conversa; os de pedido e de comanda seguem como antes', async () => {
  const { alvoDoEvento } = await plano()
  assert.deepEqual(alvoDoEvento('conversa.atualizada', { conversaId: 'c-9' }), { tipo: 'conversa', conversaId: 'c-9' })
  assert.equal(alvoDoEvento('conversa.atualizada', {}), null, 'sem conversaId não relê nada')
  assert.deepEqual(alvoDoEvento('pedido.mudou_status', { pedidoId: 'p-1' }), { tipo: 'pedido', pedidoId: 'p-1' })
  assert.deepEqual(alvoDoEvento('conversa.comanda_atualizada', { conversaId: 'c-2' }), { tipo: 'comanda', conversaId: 'c-2' })
  assert.deepEqual(alvoDoEvento('ready', null), { tipo: 'pronto' })
  assert.equal(alvoDoEvento('conversa.mensagem_recebida', { conversaId: 'c-2' }), null)
  assert.equal(alvoDoEvento(undefined, undefined), null)
})

await confere('6. no laço: a conversa atualizada fora da página 1 é lida na volta seguinte e o nome novo chega à tela', async () => {
  const s = servidor({ conversas: encerradas(250) })
  const m = await montar()
  await m.lenta()
  s.conversas = s.conversas.map((r) => (r.id === idDe(119) ? { ...r, contatoNome: 'Ana Visitante' } : r))
  s.zerar()
  await m.rapida()
  assert.deepEqual(s.paginas(), [1], 'sem o evento a volta rápida lê só a página 1')
  assert.equal(m.conversa(idDe(119)).nome, `Cliente ${idDe(119)}`)
  m.volta.atualizarConversa(idDe(119))
  s.zerar()
  await m.rapida()
  assert.deepEqual(s.paginas(), [1, 2], 'a volta depois do evento não foi até a página da conversa')
  assert.equal(m.conversa(idDe(119)).nome, 'Ana Visitante', 'o nome novo não chegou à inbox')
  s.zerar()
  await m.rapida()
  await m.rapida()
  assert.deepEqual(s.paginas(), [1, 1], 'o evento prendeu a volta rápida nas páginas da conversa')
})

await confere('6. o hook relê a conversa no evento, sem passar pela releitura de pedidos', () => {
  const hook = readFileSync(new URL('../src/aplicacao/useSincronizacaoApi.js', import.meta.url), 'utf8').replaceAll('\r', '')
  assert.match(hook, /const alvo = alvoDoEvento\(evento, dados\)/)
  assert.match(hook, /if \(alvo\.tipo === 'conversa'\) \{\s*sincronizar\.atualizarConversa\(alvo\.conversaId\)\s*ciclo\(\)\s*return\s*\}/)
})

// --- 7. #1966: conversa aberta e aba visível marcam como lida a entrada nova ---------------------
const entradaNova = (s, id, minutos) => {
  s.conversas = s.conversas.map((r) => (r.id === id ? { ...r, ultimaMensagemEm: hora(minutos), naoLidas: (r.naoLidas ?? 0) + 1 } : r))
}
await confere('7. entrada nova na conversa aberta marca como lida uma vez por lote, sem repetir a cada volta', async () => {
  const s = servidor({ conversas: inbox(3) })
  const m = await montar({ selecionada: idDe(0) })
  await m.lenta()
  assert.deepEqual(s.de('/marcar-lida'), [], 'abrir já marcou pela seleção; o laço não repete')
  entradaNova(s, idDe(0), -1)
  await m.rapida()
  assert.deepEqual(s.de('/marcar-lida'), [`/api/atendimento/conversas/${idDe(0)}/marcar-lida`])
  await m.rapida()
  await m.rapida()
  assert.equal(s.de('/marcar-lida').length, 1, 'tempestade de marcar-lida')
  entradaNova(s, idDe(0), -2)
  await m.rapida()
  assert.equal(s.de('/marcar-lida').length, 2, 'o lote seguinte não foi marcado')
})

await confere('7. aba oculta não marca; ao voltar a ficar visível, marca o que chegou', async () => {
  const s = servidor({ conversas: inbox(3) })
  const m = await montar({ selecionada: idDe(0) })
  await m.lenta()
  m.visivel = false
  entradaNova(s, idDe(0), -1)
  await m.rapida()
  assert.deepEqual(s.de('/marcar-lida'), [], 'marcou como lida com a aba oculta')
  m.visivel = true
  await m.rapida()
  assert.equal(s.de('/marcar-lida').length, 1, 'o que chegou com a aba oculta ficou sem marcar')
})

await confere('7. entrada em outra conversa e troca de conversa não chamam marcar-lida pelo laço', async () => {
  const s = servidor({ conversas: inbox(3) })
  const m = await montar({ selecionada: idDe(0) })
  await m.lenta()
  entradaNova(s, idDe(1), -1)
  await m.rapida()
  assert.deepEqual(s.de('/marcar-lida'), [], 'marcou a conversa que não está aberta')
  m.selecionada = idDe(1)
  await m.rapida()
  assert.deepEqual(s.de('/marcar-lida'), [], 'a seleção já marca; o laço repetiu')
  const sem = await montar({ selecionada: null })
  await sem.lenta()
  await sem.rapida()
  assert.deepEqual(s.de('/marcar-lida'), [])
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
