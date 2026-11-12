import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(nome, contexto, proximo) {
    if (nome.startsWith('.') && !/\.[a-z]+$/i.test(nome)) { try { return proximo(nome + '.js', contexto) } catch { /* resolução normal */ } }
    return proximo(nome, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = '';" }
    return proximo(url, contexto)
  },
})
const { casosRascunhoDaComanda } = await import('../src/aplicacao/casos/rascunhoDaComanda.js')
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
const { aplicarVinculoConfirmado } = await import('../src/dominio/vinculoCliente.js')
const { gravarVisita, lerVisita } = await import('../src/infra/api/visitaDaSessao.js')
const acao = await import('../src/aplicacao/acoes.js')
const item = { sku: 'produto', nome: 'Lasanha', estoque: 10 }
let estado = { conversas: [{ id: 'conversa', estado: 'Em atendimento', clienteId: 'cliente-a', vinculoRevisao: 4, cliente: {}, pedido: null }],
  catalogo: { cardapio: [item] }, producao: { lotes: [] }, alertasEstoque: [] }
const adicionar = () => { estado = casosRascunhoDaComanda[acao.ADICIONAR_ITEM](estado, { id: 'conversa', item, numeroPedido: 'local', agora: Date.now() }) }
adicionar()
const proposta = { ...estado.conversas[0].pedido, janela: 'janela|2026-11-13', meio: 'dinheiro-entrega' }
assert.deepEqual(proposta.identidadeProposta, { clienteId: 'cliente-a', vinculoRevisao: 4 })
estado.conversas[0].pedido = proposta
const corrigida = aplicarVinculoConfirmado(estado.conversas[0], { clienteId: 'cliente-b', clienteNome: 'Pessoa B', vinculoRevisao: 5 })
estado.conversas[0] = corrigida
adicionar()
assert.deepEqual(estado.conversas[0].pedido.identidadeProposta, proposta.identidadeProposta, 'Editar itens não troca silenciosamente a identidade da proposta')
const ref = { current: estado }
const avisos = []
const sessao = { token: 'fixture', empresa: { id: 'empresa' }, usuario: { id: 'dona' }, expiraEm: Date.now() + 60000 }
globalThis.sessionStorage = { getItem: () => JSON.stringify(sessao) }
const chamadas = []
globalThis.fetch = async (url, opcoes) => {
  chamadas.push({ url, corpo: opcoes?.body && JSON.parse(opcoes.body) })
  return Response.json({ error: { code: 'VINCULO_CLIENTE_ALTERADO', message: 'A identidade mudou.' } }, { status: 409 })
}
const criar = (antesDeEnviar) => criarAcoesComandaApi({}, { estadoRef: ref, despachar: a => avisos.push(a), ...(antesDeEnviar ? { antesDeEnviar } : {}) })
assert.ok((await criar().gerarPedido('conversa')).erro)
assert.equal(chamadas.length, 0, 'A → B é recusado antes do POST')
ref.current = { ...estado, conversas: [{ ...corrigida, clienteId: 'cliente-a', vinculoRevisao: 6, pedido: proposta }] }
assert.ok((await criar().gerarPedido('conversa')).erro)
assert.equal(chamadas.length, 0, 'ABA é recusado pela revisão, mesmo clienteId')
ref.current.conversas[0] = { ...ref.current.conversas[0], vinculoRevisao: 4, pedido: { ...proposta, identidadeProposta: undefined } }
assert.ok((await criar().gerarPedido('conversa')).erro)
assert.equal(chamadas.length, 0, 'Proposta legada sem identidade não usa fallback atual')
ref.current.conversas[0] = { ...ref.current.conversas[0], pedido: proposta }
let continuar
const emEspera = criar((_id, fazer) => new Promise(resolve => { continuar = () => resolve(fazer()) })).gerarCobranca('conversa', proposta, 'dinheiro-entrega')
ref.current.conversas[0] = { ...corrigida, pedido: { ...proposta, identidadeProposta: { clienteId: 'cliente-b', vinculoRevisao: 5 } } }
continuar()
assert.ok((await emEspera).erro)
assert.equal(chamadas.length, 0, 'A espera para assumir a conversa não substitui a proposta clicada por outra')
ref.current.conversas[0] = { ...corrigida, clienteId: 'cliente-a', vinculoRevisao: 4, pedido: proposta }
assert.ok((await criar().gerarPedido('conversa')).erro)
assert.equal(chamadas.length, 1, '409 de identidade é definitivo, sem GET recuperando pedido de outra pessoa')
assert.equal(chamadas[0].corpo.clienteId, 'cliente-a')
assert.equal(chamadas[0].corpo.vinculoRevisao, 4)
assert.deepEqual(ref.current.conversas[0].pedido.itens, proposta.itens, 'Recusa preserva o rascunho')
estado = { ...estado, conversas: [{ ...corrigida, clienteId: 'cliente-a', vinculoRevisao: 4, pedido: proposta }] }
estado = casosRascunhoDaComanda[acao.REMOVER_ITEM](estado, { id: 'conversa', sku: item.sku })
assert.equal(estado.conversas[0].pedido.itens.length, 0)
assert.equal(estado.conversas[0].pedido.janela, proposta.janela)
assert.equal(estado.conversas[0].pedido.meio, proposta.meio)
estado.conversas[0] = aplicarVinculoConfirmado(estado.conversas[0], { clienteId: 'cliente-b', clienteNome: 'Pessoa B', vinculoRevisao: 5 })
adicionar()
assert.deepEqual(estado.conversas[0].pedido.identidadeProposta, proposta.identidadeProposta, 'Lista vazia preserva identidade da proposta')
assert.equal(estado.conversas[0].pedido.janela, proposta.janela)
assert.equal(estado.conversas[0].pedido.meio, proposta.meio)
ref.current = estado
assert.ok((await criar().gerarPedido('conversa')).erro, 'Adicionar após zerar itens exige reconferência de B')
estado.conversas[0] = { ...estado.conversas[0], clienteId: 'cliente-a', vinculoRevisao: 6 }
assert.ok((await criar().gerarPedido('conversa')).erro, 'Adicionar após zerar itens exige reconferência no ABA')
const storage = new Map()
globalThis.sessionStorage = { getItem: chave => storage.get(chave) ?? null, setItem: (chave, valor) => storage.set(chave, valor) }
gravarVisita(sessao, { ultimaConversa: 'conversa', comandas: { conversa: proposta } })
assert.deepEqual(lerVisita(sessao).comandas.conversa.identidadeProposta, proposta.identidadeProposta, 'Recarga conserva o snapshot original, sem substituir pelo cliente atual')
gravarVisita(sessao, { comandas: { conversa: { ...proposta, identidadeProposta: undefined } } })
assert.equal(lerVisita(sessao).comandas.conversa.identidadeProposta, undefined, 'Recarga de legado não inventa versão')
console.log('Proposta: captura original, A→B, ABA, legado, espera assíncrona e 409 definitivo passaram; rascunho preservado.')
const reconferir = (conversa, identidade) => casosRascunhoDaComanda[acao.RECONFERIR_PESSOA_COMANDA](
  { ...estado, conversas: [conversa] }, { id: 'conversa', identidade },
).conversas[0]
const atual = { ...corrigida, pedido: proposta, rascunho: 'Mensagem preservada' }
const recuperada = reconferir(atual, { clienteId: 'cliente-b', vinculoRevisao: 5 })
assert.deepEqual(recuperada.pedido, { ...proposta, identidadeProposta: { clienteId: 'cliente-b', vinculoRevisao: 5 } })
assert.equal(recuperada.rascunho, atual.rascunho)
assert.equal(reconferir(atual, { clienteId: 'cliente-a', vinculoRevisao: 4 }), atual, 'Conferência aberta antes da troca não altera proposta')
assert.equal(reconferir({ ...atual, clienteId: null }, { clienteId: null, vinculoRevisao: 5 }).pedido, proposta, 'Sem cadastro não confirma identidade vazia')
assert.equal(reconferir({ ...atual, vinculoRevisao: 7 }, { clienteId: 'cliente-b', vinculoRevisao: 5 }).pedido, proposta, 'ABA durante conferência não troca snapshot')
const persistida = { ...atual, pedido: { ...proposta, pedidoId: 'persistido' } }
assert.equal(reconferir(persistida, { clienteId: 'cliente-b', vinculoRevisao: 5 }), persistida)
for (const identidadeProposta of [undefined, { clienteId: null, vinculoRevisao: 1 }]) {
  const resultado = reconferir({ ...atual, pedido: { ...proposta, identidadeProposta } }, { clienteId: 'cliente-b', vinculoRevisao: 5 })
  assert.deepEqual(resultado.pedido.itens, proposta.itens)
  assert.deepEqual(resultado.pedido.identidadeProposta, { clienteId: 'cliente-b', vinculoRevisao: 5 })
}
console.log('Reconferência explícita preserva itens/janela/meio/rascunho; legado e sem vínculo anterior recuperam; troca/ABA durante confirmação recusados.')
