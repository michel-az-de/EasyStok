/* eslint-disable no-console */
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
registerHooks({
  resolve(s, c, next) {
    if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução padrão */ } }
    return next(s, c)
  },
  load(url, c, next) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return next(url, c)
  },
})
const { criarAcoesModelosApi } = await import('../src/aplicacao/api/modelos.js')
const { reducer } = await import('../src/aplicacao/reducer.js')
const { tentativaModelo, limparTentativaModelo } = await import('../src/infra/api/modelosApi.js')
const { permissaoDeEscrita } = await import('../src/dominio/janela.js')
const storage = new Map()
globalThis.sessionStorage = { getItem: k => storage.get(k) ?? null, setItem: (k, v) => storage.set(k, v), removeItem: k => storage.delete(k) }
let sessao = { token: 'sintetico', empresa: { id: 'a' }, usuario: { id: 'u' }, expiraEm: Date.now() + 60000 }
sessionStorage.setItem('easystok.sessao', JSON.stringify(sessao))
const dados = { nome: 'retomar', idioma: 'pt_BR', parametros: ['Ana'] }
let estado = { conversas: [{ id: 'c1', estado: 'Em atendimento', canal: 'WhatsApp', mensagens: [],
  janelaExpiraEm: '2026-10-08T00:00:00Z', cliente: {} }] }
const criar = () => criarAcoesModelosApi({ despachar: a => { estado = reducer(estado, a) } })
let api = criar()
const chamadas = []
let liberar
globalThis.fetch = async (url, options) => {
  chamadas.push({ url, corpo: JSON.parse(options.body) })
  return new Promise(resolve => { liberar = resolve })
}
const resposta = (extra = {}) => new Response(JSON.stringify({ data: {
  id: chamadas.at(-1).corpo.tentativaId, direcao: 'Saida', autor: 'Dona', tipoConteudo: 'Modelo', status: 'Enviada',
  texto: 'Olá Ana', modelo: dados, enviadaEm: '2026-10-10T12:00:00Z', ...extra,
} }), { status: 200, headers: { 'Content-Type': 'application/json' } })
const primeiro = api.enviarModelo('c1', dados)
const duplo = api.enviarModelo('c1', dados)
await new Promise(r => setTimeout(r, 0))
assert.equal(chamadas.length, 1)
assert.equal(estado.conversas[0].mensagens.length, 0, 'não inventa envio antes da API')
assert.equal(chamadas[0].url, '/api/atendimento/conversas/c1/mensagens/modelo')
assert.equal(chamadas[0].corpo.texto, undefined)
liberar(resposta())
await Promise.all([primeiro, duplo])
assert.equal(estado.conversas[0].mensagens.length, 1)
assert.equal(estado.conversas[0].mensagens[0].modelo, true)
assert.equal(permissaoDeEscrita(estado.conversas[0], Date.parse('2026-10-10T15:00:00Z')).pode, false)
console.log('ok rota própria, dupla ação, histórico real, selo e janela preservada')

globalThis.fetch = async (_url, options) => { chamadas.push({ corpo: JSON.parse(options.body) }); throw new TypeError('resposta perdida') }
await assert.rejects(api.enviarModelo('c1', dados))
const tentativa = chamadas.at(-1).corpo.tentativaId
api = criar() // nova montagem conserva a tentativa até receber resposta
globalThis.fetch = async (_url, options) => { chamadas.push({ corpo: JSON.parse(options.body) }); return resposta({ status: 'Pendente' }) }
await api.enviarModelo('c1', dados)
assert.equal(chamadas.at(-1).corpo.tentativaId, tentativa)
assert.equal(estado.conversas[0].mensagens.at(-1).status, 'enviando')
await api.enviarModelo('c1', dados)
assert.equal(chamadas.at(-1).corpo.tentativaId, tentativa, 'pendente não gera outra tentativa')
assert.equal(estado.conversas[0].mensagens.length, 2, 'replay atualiza a mesma mensagem')
api.prepararOutroModelo('c1')
assert.notEqual(tentativaModelo('c1', dados), tentativa, 'outro envio exige ação explícita')
console.log('ok retomada sem duplicação e intenção pendente preservada')

const propria = tentativaModelo('c1', dados)
sessao = { ...sessao, empresa: { id: 'b' } }
sessionStorage.setItem('easystok.sessao', JSON.stringify(sessao))
assert.notEqual(tentativaModelo('c1', dados), propria)
limparTentativaModelo('c1')
globalThis.fetch = async () => new Response(JSON.stringify({ error: { code: 'RECUSADO', message: 'Modelo revogado' } }), { status: 400 })
const antes = JSON.stringify(estado)
await assert.rejects(api.enviarModelo('c1', dados), /Modelo revogado/)
assert.equal(JSON.stringify(estado), antes)
console.log('ok identidade da sessão e recusa sem alteração local')

// #1966 (frente D da #1889): sem a conexão do WhatsApp, a lista e o envio de modelo voltam 409 WHATSAPP_NAO_CONECTADO.
// A frase é a da API e o caminho é Integrações; nada saiu, então não há "Ao tentar novamente..." no envio.
const { erroDoModelo } = await import('../src/dominio/falhaDeEnvio.js')
const FRASE = 'O envio real do WhatsApp não está conectado. Configure a conexão em Integrações.'
globalThis.fetch = async () => new Response(JSON.stringify({ error: { code: 'WHATSAPP_NAO_CONECTADO', message: FRASE } }), { status: 409 })
const semConexaoNoEnvio = await api.enviarModelo('c1', { ...dados, parametros: ['Bia'] }).then(() => null, (e) => e)
assert.equal(semConexaoNoEnvio?.codigo, 'WHATSAPP_NAO_CONECTADO')
assert.deepEqual(erroDoModelo(semConexaoNoEnvio, { envio: true }), { texto: FRASE, integracoes: true })
const semConexaoNaLista = await api.listarModelos().then(() => null, (e) => e)
assert.deepEqual(erroDoModelo(semConexaoNaLista), { texto: FRASE, integracoes: true })
globalThis.fetch = async () => new Response(JSON.stringify({ error: { code: 'MODELOS_INDISPONIVEIS', message: 'A Meta não respondeu.' } }), { status: 502 })
const outraFalha = await api.enviarModelo('c1', { ...dados, parametros: ['Caio'] }).then(() => null, (e) => e)
const doEnvio = erroDoModelo(outraFalha, { envio: true })
assert.equal(doEnvio.integracoes, false)
assert.match(doEnvio.texto, /^A Meta não respondeu\. Ao tentar novamente, esta mesma tentativa será consultada sem duplicar o envio\.$/)
assert.deepEqual(erroDoModelo(outraFalha), { texto: 'A Meta não respondeu.', integracoes: false })
const modal = readFileSync(new URL('../src/features/atendimento/ModalEnviarModelo.jsx', import.meta.url), 'utf8')
assert.doesNotMatch(modal, /Ao tentar novamente/, 'a frase do envio mora no domínio, não no modal')
assert.match(modal, /erroDoModelo\(e, \{ envio: true \}\)/)
assert.match(modal, /erroDoModelo\(e\)/)
assert.match(modal, /semConexao && \(podeVerIntegracoes\(acoes\)\s*\? <a href=\{hashDoModulo\('configuracoes', 'integracoes'\)\}>/)
console.log('ok 409 sem WhatsApp conectado: frase da API e caminho para Integrações, sem repetir a tentativa')

// #1408: "Chamar no WhatsApp" para cliente cadastrado que nunca escreveu. Rota própria com o cliente; a mesma
// proteção de tentativa (dupla ação e resposta perdida não duplicam) e o mesmo 409 sem a conexão do WhatsApp.
const { clientesParaChamar } = await import('../src/dominio/cliente.js')
const cadastrados = [
  { id: 'k1', nome: 'Ana Souza', telefone: '(11) 98888-7777' },
  { id: 'k2', nome: 'Bia Lima', telefone: '' },
  { id: 'k3', nome: 'Caio Reis', telefone: '11977776666' },
  { id: 'k4', nome: 'Duda Melo', telefone: '11966665555' },
]
const naTela = [
  { id: 'c3', clienteId: 'k3', estado: 'Em atendimento' },
  { id: 'c4', clienteId: 'k4', estado: 'Encerrado' },
]
assert.deepEqual(clientesParaChamar(cadastrados, naTela).map(c => c.id), ['k1', 'k4'],
  'sem telefone e com conversa aberta ficam de fora; conversa encerrada não impede chamar de novo')
const iniciadas = []
let liberarInicio
globalThis.fetch = async (url, options) => {
  iniciadas.push({ url, corpo: JSON.parse(options.body) })
  return new Promise(resolve => { liberarInicio = resolve })
}
const ana = cadastrados[0]
const chamada = api.iniciarConversaComModelo(ana, { ...dados, parametros: ['Ana'] })
const dupla = api.iniciarConversaComModelo(ana, { ...dados, parametros: ['Ana'] })
await new Promise(r => setTimeout(r, 0))
assert.equal(iniciadas.length, 1, 'dupla ação não chama duas vezes')
assert.equal(iniciadas[0].url, '/api/atendimento/conversas/iniciar')
assert.equal(iniciadas[0].corpo.clienteId, 'k1')
assert.ok(iniciadas[0].corpo.tentativaId)
liberarInicio(new Response(JSON.stringify({ data: { conversaId: 'nova', mensagem: {
  id: iniciadas[0].corpo.tentativaId, direcao: 'Saida', autor: 'Dona', tipoConteudo: 'Modelo', status: 'Enviada',
  texto: 'Olá Ana', modelo: dados, enviadaEm: '2026-10-23T12:00:00Z',
} } }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
const [iniciada] = await Promise.all([chamada, dupla])
assert.equal(iniciada.conversaId, 'nova')
globalThis.fetch = async (_url, options) => { iniciadas.push({ corpo: JSON.parse(options.body) }); throw new TypeError('resposta perdida') }
await assert.rejects(api.iniciarConversaComModelo(ana, { ...dados, parametros: ['Ana'] }))
const perdida = iniciadas.at(-1).corpo.tentativaId
assert.notEqual(perdida, iniciadas[0].corpo.tentativaId, 'enviada com sucesso libera a próxima tentativa')
await assert.rejects(api.iniciarConversaComModelo(ana, { ...dados, parametros: ['Ana'] }))
assert.equal(iniciadas.at(-1).corpo.tentativaId, perdida, 'resposta perdida repete a mesma tentativa')
globalThis.fetch = async () => new Response(JSON.stringify({ error: { code: 'WHATSAPP_NAO_CONECTADO', message: FRASE } }), { status: 409 })
const semConexaoNoInicio = await api.iniciarConversaComModelo(cadastrados[3], { ...dados, parametros: ['Duda'] }).then(() => null, (e) => e)
assert.deepEqual(erroDoModelo(semConexaoNoInicio, { envio: true }), { texto: FRASE, integracoes: true })
assert.match(modal, /Chamar no WhatsApp/)
assert.match(modal, /iniciarConversaComModelo\(cliente, /)
const balcao = readFileSync(new URL('../src/features/atendimento/ChamarClientes.jsx', import.meta.url), 'utf8')
assert.match(balcao, /clientesParaChamar\(/)
assert.match(balcao, /<ModalEnviarModelo\s+cliente=\{/)
console.log('ok chamar no WhatsApp: rota com o cliente, sem duplicar, 409 sem conexão e filtro dos cadastrados')
