// #2921 (revisão da PR #2940): (A) pedido entregue ou cancelado não trava a janela do Novo pedido: ela começa
// comanda nova pelo mesmo `novaComanda` do Atendimento; (B) a retenção da conversa iniciada pela equipe vale só
// enquanto o servidor não a lista; (C) o GET por id de "Iniciar" não rouba a seleção que a usuária já trocou.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
  load(url, c, next) { return url.endsWith('/infra/fonteDados.js') ? { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" } : next(url, c) },
})
globalThis.sessionStorage = { getItem: () => null, setItem() {}, removeItem() {} }
const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const j = await import('../src/aplicacao/janelaDoPedido.js')
const acao = await import('../src/aplicacao/acoes.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const { conversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const USUARIO = { id: 'u-1', nome: 'Thati' }
const resumo = (id) => ({
  id, canal: 'WhatsApp', contatoNome: `Cliente ${id}`, contatoIdExterno: `55119${id}`, clienteId: null, situacao: 'Automatica',
  assumidaPorUsuarioId: null, naoLidas: 0, dentroDaJanela: true, ultimaMensagemEm: new Date().toISOString(), ultimaMensagemTexto: 'oi',
  aguardaResposta: false, pedidoEmAndamentoId: null,
})
const estadoCom = (...rs) => estadoInicial({ conversas: rs.map((r) => conversaDaApi(r, [], USUARIO)), catalogo: { janelas: [] }, regras: [], modoAgente: 'simulado' })
const sincronizar = (estado, rs, extra = {}) => reducer(estado, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: rs.map((r) => conversaDaApi(r, [], USUARIO)), ...extra })

// A. Pedido encerrado não trava; em andamento trava.
{
  const com = (estado) => ({ pedido: { pedidoId: 'p', estado, itens: [{ sku: 'a', qtd: 1 }] } })
  assert.equal(j.pedidoJaCriadoNaConversa(com('entregue')), false, 'entregue libera')
  assert.equal(j.pedidoJaCriadoNaConversa(com('cancelado')), false, 'cancelado libera')
  assert.equal(j.pedidoJaCriadoNaConversa(com('pago')), true, 'em andamento trava')
  assert.equal(j.pedidoJaCriadoNaConversa({ pedido: { itens: [] } }), false)
  assert.equal(j.comandaEmAndamento(com('entregue')), false, 'a comanda de um pedido encerrado não é "em montagem"')
  assert.equal(j.comandaEmAndamento(com('pago')), true)
  assert.equal(typeof j.precisaNovaComanda, 'function', 'precisaNovaComanda ainda não existe')
  assert.equal(j.precisaNovaComanda(com('entregue')), true)
  assert.equal(j.precisaNovaComanda(com('cancelado')), true)
  assert.equal(j.precisaNovaComanda(com('pago')), false)
  assert.equal(j.precisaNovaComanda({ pedido: { itens: [] } }), false)
  // o mesmo caminho do Atendimento: NOVA_COMANDA troca o pedido encerrado por rascunho, e o pedido encerrado vindo do servidor não o apaga
  let estado = estadoCom(resumo('a'))
  estado = { ...estado, conversas: estado.conversas.map((c) => ({ ...c, pedido: { ...com('entregue').pedido, identidadeProposta: null } })) }
  estado = reducer(estado, { tipo: acao.NOVA_COMANDA, id: 'a', numeroPedido: 9 })
  assert.equal(estado.conversas[0].pedido.pedidoId, undefined, 'virou rascunho')
  assert.equal(j.pedidoJaCriadoNaConversa(estado.conversas[0]), false)
  const jsx = fonte('app/NovoPedidoJanela.jsx')
  assert.match(jsx, /precisaNovaComanda\(conversa\)/, 'a janela detecta o pedido encerrado')
  assert.match(jsx, /acoes\.novaComanda\(janela\.conversaId\)/, 'e começa comanda nova pelo mesmo caminho do Atendimento')
}

// B. A retenção cai quando o servidor passa a listar a conversa.
{
  const iniciada = conversaDaApi(resumo('nova'), [], USUARIO)
  let estado = reducer(estadoCom(resumo('a')), { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [conversaDaApi(resumo('a'), [], USUARIO), iniciada], semMensagens: ['nova'] })
  estado = reducer(estado, { tipo: acao.SELECIONAR_CONVERSA, id: 'nova', reter: true })
  estado = sincronizar(estado, [resumo('a')])
  assert.equal(estado.selecaoRetida, 'nova', 'o servidor ainda não a lista: continua retida')
  estado = sincronizar(estado, [resumo('a'), resumo('nova')])
  assert.equal(estado.selecaoRetida ?? null, null, 'o servidor a listou: sem retenção')
  estado = sincronizar(estado, [resumo('a')])
  assert.ok(!estado.conversas.some((c) => c.id === 'nova'), 'arquivada por outra atendente: sai, não fica a cópia congelada')
  assert.equal(estado.selecionadaId, null, '#2135: a escolhida que some vira nenhuma')
}

// C. O GET tardio de "Iniciar" respeita a seleção que a usuária já trocou.
{
  const api = fonte('aplicacao/acoesApi.js')
  const trecho = api.slice(api.indexOf('selecionarIniciada:'), api.indexOf('assumirAtendimento:'))
  assert.match(trecho, /selecionadaId/, 'selecionarIniciada confere a seleção depois da espera')
}
console.log('prova-2921-pedido-encerrado: 3 blocos ok')
