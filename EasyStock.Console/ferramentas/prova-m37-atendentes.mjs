/* eslint-disable no-console */
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* resolução padrão */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})
const { situacaoConversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { combinaResponsavel, conversasDoBalcao, conversasEncerradasDoBalcao, conversasBloqueadasDoBalcao } = await import('../src/dominio/conversa.js')
const { estadoDaLinha } = await import('../src/dominio/linhaDoBalcao.js')
const { PAUSA_POR_OUTRO } = await import('../src/dominio/automatico.js')
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const { reducer } = await import('../src/aplicacao/reducer.js')
const usuario = { id: 'a', nome: 'Ana' }, outro = { id: 'b', nome: 'Ana' }
const conversa = (id, dono = null) => ({ id, nome: id, canal: 'WhatsApp', mensagens: [], cliente: { tags: [] }, ...situacaoConversaDaApi({ situacao: 'Assumida', assumidaPorUsuarioId: dono, assumidaPorNome: dono ? 'Ana' : null }, usuario) })
const minha = conversa('minha', usuario.id), alheia = conversa('outra', outro.id), livre = conversa('livre')
const filtros = { aba: 'todas', canais: [], busca: '', responsavel: 'todas' }
assert.deepEqual(conversasDoBalcao([minha, alheia, livre], filtros).map(c => c.id), ['minha', 'outra', 'livre'])
assert.deepEqual(conversasDoBalcao([minha, alheia, livre], { ...filtros, responsavel: 'minhas' }, Date.now(), {}, true, [], null, usuario).map(c => c.id), ['minha'])
assert.equal(combinaResponsavel(alheia, 'minhas', outro), true, 'homônimos têm identidades distintas')
assert.equal(combinaResponsavel(alheia, 'minhas', usuario), false)
assert.equal(combinaResponsavel({ ...alheia, assumidaPorUsuarioId: null }, 'minhas', usuario), false, 'API não identifica a atendente pelo nome')
assert.equal(combinaResponsavel({ responsavel: 'Thatiane' }, 'minhas', { nome: 'Thatiane' }), true, 'compatibilidade da demonstração')
assert.deepEqual(conversasDoBalcao([minha, alheia, livre], { ...filtros, responsavel: 'sem-dono' }).map(c => c.id), ['livre'])
assert.equal(estadoDaLinha({ responsavel: 'Beatriz' }, { pausado: PAUSA_POR_OUTRO }).texto, 'Beatriz')
assert.equal(situacaoConversaDaApi({ situacao: 'Assumida', assumidaPorUsuarioId: 'b' }, usuario).responsavel, 'Outro atendente', 'resposta antiga continua legível')
const fechadas = [ { ...minha, estado: 'Encerrado' }, { ...alheia, estado: 'Encerrado' } ]
assert.deepEqual(conversasEncerradasDoBalcao(fechadas, { ...filtros, responsavel: 'minhas' }, usuario).map(c => c.id), ['minha'])
assert.deepEqual(conversasBloqueadasDoBalcao([{ ...minha, bloqueio: true }, { ...alheia, bloqueio: true }], { ...filtros, responsavel: 'minhas' }, usuario).map(c => c.id), ['minha'])
assert.equal(conversasDoBalcao([minha], { ...filtros, responsavel: 'minhas', canais: ['E-mail'] }, Date.now(), {}, true, [], null, usuario).length, 0)
assert.equal(conversasDoBalcao([minha], { ...filtros, responsavel: 'minhas', busca: 'outra' }, Date.now(), {}, true, [], null, usuario).length, 0)
console.log('ok Todas por padrão, Minhas por ID, Sem dono, nomes, homônimos, canais, busca e grupos')
globalThis.sessionStorage = { getItem: () => JSON.stringify({ usuario, expiraEm: Date.now() + 60000 }) }
const resposta = (data, status = 200) => new Response(JSON.stringify({ data }), { status, headers: { 'Content-Type': 'application/json' } })
let estado = { conversas: [livre], automaticoPausado: {}, filtros, rascunhos: { livre: 'rascunho preservado' } }
const api = comApi({}, { despachar: a => { estado = reducer(estado, a) }, agoraRef: { current: Date.now() }, estadoRef: { current: estado } })
let confirmar
globalThis.fetch = async () => new Promise(resolve => { confirmar = resolve })
const assumindo = api.assumirAtendimento('livre')
await new Promise(resolve => setTimeout(resolve, 0))
assert.equal(estado.conversas[0].assumidaPorUsuarioId, null)
confirmar(resposta({ situacao: 'Assumida', assumidaPorUsuarioId: 'a', naoLidas: 0 }))
await assumindo
assert.equal(combinaResponsavel(estado.conversas[0], 'minhas', usuario), true)
assert.equal(estado.conversas[0].responsavel, 'Ana')
globalThis.fetch = async () => resposta({ situacao: 'Automatica', assumidaPorUsuarioId: null, naoLidas: 0 })
await api.devolverAutomatico('livre')
assert.equal(combinaResponsavel(estado.conversas[0], 'sem-dono', usuario), true)
assert.equal(estado.rascunhos.livre, 'rascunho preservado')
for (const status of [403, 409, 500]) {
  globalThis.fetch = async () => resposta(null, status)
  await api.assumirAtendimento('livre')
  assert.equal(estado.conversas[0].assumidaPorUsuarioId, null, 'recusa não muda a atribuição')
}
globalThis.fetch = async url => { assert.equal(url, '/api/atendimento/atendentes'); return resposta([{ usuarioId: 'a', nome: 'Ana' }]) }
assert.equal((await api.listarAtendentes())[0].nome, 'Ana')
console.log('ok atribuição confirmada, liberação, recusas, rascunho e lista real de atendentes')
