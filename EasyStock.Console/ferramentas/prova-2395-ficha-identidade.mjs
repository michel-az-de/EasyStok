import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(nome, contexto, proximo) { if (nome.startsWith('.') && !/\.[a-z]+$/i.test(nome)) { try { return proximo(nome + '.js', contexto) } catch { /* resolução normal */ } } return proximo(nome, contexto) },
  load(url, contexto, proximo) { if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API=true; export const API_BASE='';" }; return proximo(url, contexto) },
})
const { criarAcoesClienteApi } = await import('../src/aplicacao/api/cliente.js')
const { identidadeDaEdicao } = await import('../src/dominio/identidadeDaEdicao.js')
const { aplicarVinculoConfirmado } = await import('../src/dominio/vinculoCliente.js')
const acao = await import('../src/aplicacao/acoes.js')
const base = { id: 'conversa', clienteId: 'a', vinculoRevisao: 4, nome: 'Pessoa A', canal: 'WhatsApp', cliente: { telefoneCanal: '11999999999', enderecoCapturado: 'Rua A, 10, Centro, 01001-000' } }
const snapshot = identidadeDaEdicao(base)
const ref = { current: { conversas: [base] } }, chamadas = [], despachos = []
const api = criarAcoesClienteApi({ estadoRef: ref, despachar: a => { despachos.push(a); if (a.tipo === acao.VINCULO_CLIENTE_DA_API) ref.current.conversas[0] = aplicarVinculoConfirmado(ref.current.conversas[0], a.vinculo) } })
globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'fixture', empresa: { id: 'empresa' }, usuario: { id: 'dona' }, expiraEm: Date.now() + 60000 }) }
globalThis.fetch = async (url, opcoes) => { chamadas.push({ url, corpo: opcoes?.body && JSON.parse(opcoes.body) }); return Response.json({ error: { code: 'VALIDATION_ERROR', message: 'Recusa controlada.' } }, { status: 400 }) }
const salvar = identidade => [() => api.salvarCadastroRapido(base.id, { nome: 'Nome digitado', telefone: '11999999999' }, 0, identidade), () => api.editarDadoCliente(base.id, 'nome', 'Nome digitado', 0, identidade), () => api.cadastrarEndereco(base.id, identidade, base.cliente.enderecoCapturado)]
for (const atual of [{ ...base, clienteId: 'b', vinculoRevisao: 5 }, { ...base, vinculoRevisao: 6 }, { ...base, clienteId: null, vinculoRevisao: 5 }]) {
  ref.current.conversas[0] = atual
  for (const executar of salvar(snapshot)) assert.equal((await executar()).codigo, 'VINCULO_CLIENTE_ALTERADO')
}
assert.equal(chamadas.length, 0, 'A→B, ABA e retirada não escrevem nem renomeiam lead local')
assert.equal(despachos.length, 0)
ref.current.conversas[0] = base
for (const executar of salvar(undefined)) assert.equal((await executar()).codigo, 'VINCULO_CLIENTE_ALTERADO')
for (const executar of salvar(snapshot)) await executar()
assert.equal(chamadas.length, 3)
for (const chamada of chamadas) assert.deepEqual({ clienteId: chamada.corpo.clienteId, vinculoRevisao: chamada.corpo.vinculoRevisao }, { clienteId: 'a', vinculoRevisao: 4 })
const semCadastro = { ...base, clienteId: null, vinculoRevisao: 0 }
ref.current.conversas[0] = semCadastro
await api.salvarCadastroRapido(base.id, { nome: 'Pessoa nova', telefone: '11999999999' }, 0, identidadeDaEdicao(semCadastro))
assert.equal(chamadas.at(-1).corpo.clienteId, null, 'Null explícito é válido na criação sem vínculo')
let responder
globalThis.fetch = (url, opcoes) => {
  chamadas.push({ url, corpo: opcoes?.body && JSON.parse(opcoes.body) })
  if (opcoes?.method === 'POST') return new Promise(resolve => { responder = resolve })
  return Promise.resolve(Response.json({ data: { cliente: { id: 'novo', nome: 'Pessoa nova' }, enderecos: [] } }))
}
const resultado = { clienteId: 'novo', nome: 'Pessoa nova', novo: true, vinculoRevisao: 1 }
const pendente = api.salvarCadastroRapido(base.id, { nome: 'Pessoa nova', telefone: '11999999999' }, 0, identidadeDaEdicao(semCadastro))
while (!responder) await new Promise(resolve => setTimeout(resolve, 0))
ref.current.conversas[0] = { ...semCadastro, clienteId: 'novo', vinculoRevisao: 1 }
responder(Response.json({ data: resultado }))
assert.equal(await pendente, undefined, 'Polling do próprio resultado antes da resposta não transforma sucesso em conflito')
ref.current.conversas[0] = semCadastro
responder = null
const antiga = api.salvarCadastroRapido(base.id, { nome: 'Pessoa nova', telefone: '11999999999' }, 0, identidadeDaEdicao(semCadastro))
while (!responder) await new Promise(resolve => setTimeout(resolve, 0))
ref.current.conversas[0] = { ...semCadastro, clienteId: 'novo', vinculoRevisao: 3 }
responder(Response.json({ data: resultado }))
assert.match((await antiga).erro, /vínculo mudou depois/)
assert.equal(ref.current.conversas[0].vinculoRevisao, 3, 'Resposta antiga não desfaz ABA posterior ao commit')
console.log('Ficha: três ações recusam A→B/ABA/retirada/legado; snapshot original no POST; null explícito válido; sincronização do próprio sucesso aceita e ABA posterior preservado.')
