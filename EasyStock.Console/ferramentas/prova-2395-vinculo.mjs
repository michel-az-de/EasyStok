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
const { criarAcoesClienteApi } = await import('../src/aplicacao/api/cliente.js')
const { casosApi } = await import('../src/aplicacao/casos/api.js')
const { mesclarDoServidor } = await import('../src/aplicacao/reducer/mesclaDoServidor.js')
const { aplicarVinculoConfirmado } = await import('../src/dominio/vinculoCliente.js')
const { conversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const recarregada = conversaDaApi({ id: 'recarregada', clienteId: null, contatoNome: 'Nome antes da retirada', contatoIdExterno: '5511999999999', canal: 'WhatsApp', situacao: 'Assumida', vinculoRevisao: 1, vinculoEmRevisao: true }, [], { id: 'operador' })
assert.equal(recarregada.nome, 'Identidade em revisão', 'Recarregar não volta a atribuir a identidade retirada')
assert.equal(recarregada.vinculoEmRevisao, true)
assert.equal(conversaDaApi({ id: 'nova', contatoNome: 'Visitante informado', canal: 'WhatsApp' }, [], {}).nome, 'Visitante informado')
const sessao = { token: 'fixture', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 60000 }
globalThis.sessionStorage = { getItem: () => JSON.stringify(sessao) }
const conversa = { id: 'conversa', clienteId: 'cliente', vinculoRevisao: 0, nome: 'Pessoa errada', cliente: { daApi: true, notas: ['nota antiga'], telefoneCanal: '5511999999999' }, mensagens: [{ id: 'm1', texto: 'Mensagem preservada' }], pedido: { itens: [{ sku: 'lasanha', quantidade: 2 }] }, rascunho: 'Rascunho preservado' }
const outra = { ...conversa, id: 'outra' }
const estadoRef = { current: { conversas: [conversa, outra], rascunhos: { conversa: 'Texto não enviado' } } }
const despachar = acao => { estadoRef.current = casosApi[acao.tipo](estadoRef.current, acao) }
let responderDossie
globalThis.fetch = async (url) => {
  if (url.endsWith('/dossie')) return new Promise(resolve => { responderDossie = () => resolve(Response.json({ data: { cliente: { id: 'cliente', nome: 'Pessoa errada' }, enderecos: [], tags: [], notas: [] } })) })
  if (url.endsWith('/vinculo-cliente')) return Response.json({ data: { clienteId: null, clienteNome: null, vinculoRevisao: 1 } })
  throw new Error(`Chamada inesperada: ${url}`)
}
const acoes = criarAcoesClienteApi({ despachar, estadoRef })
const leituraAntiga = acoes.carregarClienteDaConversa('conversa')
await new Promise(resolve => setImmediate(resolve))
assert.equal(typeof responderDossie, 'function')
await acoes.corrigirVinculoClienteConversa('conversa', { acao: 'retirar', vinculoRevisao: 0, requestId: 'intencao' })
responderDossie()
await leituraAntiga
const corrigida = estadoRef.current.conversas[0]
assert.equal(corrigida.clienteId, null, 'Dossiê anterior em voo não restaura o vínculo retirado')
assert.equal(corrigida.nome, 'Identidade em revisão')
assert.equal(corrigida.vinculoRevisao, 1)
assert.deepEqual(corrigida.mensagens, conversa.mensagens)
assert.deepEqual(corrigida.pedido, conversa.pedido)
assert.equal(corrigida.rascunho, conversa.rascunho)
assert.equal(estadoRef.current.rascunhos.conversa, 'Texto não enviado')
assert.deepEqual(estadoRef.current.conversas[1], outra, 'Outra conversa mantém ficha, notas e vínculo')
assert.equal(mesclarDoServidor(corrigida, conversa).clienteId, null, 'Polling anterior não religa cadastro')
assert.equal(mesclarDoServidor(corrigida, { ...conversa, clienteId: null, vinculoRevisao: 1 }).nome, 'Identidade em revisão')
const maisNova = mesclarDoServidor(corrigida, { ...conversa, clienteId: 'novo', vinculoRevisao: 2 })
assert.equal(maisNova.clienteId, 'novo', 'Revisão posterior prevalece')
assert.equal(aplicarVinculoConfirmado(corrigida, { clienteId: 'cliente', vinculoRevisao: 0 }), corrigida)
const restaurada = aplicarVinculoConfirmado(corrigida, { clienteId: 'cliente', clienteNome: 'Pessoa conferida', vinculoRevisao: 2 })
assert.equal(restaurada.nome, 'Pessoa conferida')
assert.deepEqual(restaurada.pedido, conversa.pedido)
console.log('Vínculo: dossiê atrasado, polling, revisão e preservação passaram. Prova determinística sem provedor externo.')
