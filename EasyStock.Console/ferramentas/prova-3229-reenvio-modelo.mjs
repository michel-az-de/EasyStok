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
// #3229: o servidor é idempotente por TentativaId (#1599) e devolve a linha Falhou antiga sem tentar de novo.
// Certo para envio INCERTO (pode ter saído): mantém a tentativa. Errado para falha DEFINITIVA: o usuário ficava preso.
const { falhaDefinitiva } = await import('../src/dominio/falhaDeEnvio.js').catch(() => ({}))
const storage = new Map()
globalThis.sessionStorage = { getItem: k => storage.get(k) ?? null, setItem: (k, v) => storage.set(k, v), removeItem: k => storage.delete(k) }
sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'sintetico', empresa: { id: 'a' }, usuario: { id: 'u' }, expiraEm: Date.now() + 60000 }))
const dados = { nome: 'retomar', idioma: 'pt_BR', parametros: ['Ana'] }
let estado = { conversas: [{ id: 'c1', estado: 'Em atendimento', canal: 'WhatsApp', mensagens: [], janelaExpiraEm: '2026-10-08T00:00:00Z', cliente: {} }] }
const api = criarAcoesModelosApi({ despachar: a => { estado = reducer(estado, a) } })
const clienteApi = criarAcoesModelosApi({ despachar: () => {} })
const enviados = []
// Frases reais do C#: Mensagem.RegistrarFalhaEnvio (prefixo da incerta), EnviarModeloConversaUseCase (AvisoEnvioIncerto e
// recusa permanente) e ClassificadorFalhaEnvio.MotivoCodigoMeta (temporária).
const INCERTA = 'Envio incerto (sem resposta do canal): Confira a conversa no WhatsApp antes de iniciar outro envio.'
const PERMANENTE = 'O WhatsApp não confirmou o envio deste modelo. Confira a conexão e o modelo antes de tentar novamente.'
const TEMPORARIA = 'Falha no canal (código Meta 131000).'
const responder = (status, erro) => async (_url, options) => {
  const corpo = JSON.parse(options.body)
  enviados.push(corpo.tentativaId)
  return new Response(JSON.stringify({ data: {
    id: corpo.tentativaId, direcao: 'Saida', autor: 'Dona', tipoConteudo: 'Modelo', status, ...(erro ? { erro } : {}),
    texto: 'Olá Ana', modelo: dados, enviadaEm: '2026-10-10T12:00:00Z',
  } }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
// Duas chamadas seguidas com a mesma resposta: a segunda leva a tentativa que sobrou da primeira.
async function repetir(chamar, status, erro) {
  enviados.length = 0
  globalThis.fetch = responder(status, erro)
  await chamar(); await chamar()
  return enviados[0] === enviados[1]
}
const porConversa = () => api.enviarModelo('c1', dados)
const porCliente = () => clienteApi.iniciarConversaComModelo({ id: 'cli1' }, dados).catch(() => {})
// iniciarConversaComModelo devolve { mensagem }; a resposta simulada serve ao envio por conversa, então o ramo do cliente usa a sua.
const respostaDoCliente = (status, erro) => async (_url, options) => {
  const corpo = JSON.parse(options.body)
  enviados.push(corpo.tentativaId)
  return new Response(JSON.stringify({ data: { conversaId: 'c9', mensagem: {
    id: corpo.tentativaId, direcao: 'Saida', autor: 'Dona', tipoConteudo: 'Modelo', status, ...(erro ? { erro } : {}),
    texto: 'Olá Ana', modelo: dados, enviadaEm: '2026-10-10T12:00:00Z',
  } } }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
async function repetirCliente(status, erro) {
  enviados.length = 0
  globalThis.fetch = respostaDoCliente(status, erro)
  await porCliente(); await porCliente()
  return enviados[0] === enviados[1]
}

assert.equal(typeof falhaDefinitiva, 'function', 'falhaDefinitiva exportada por dominio/falhaDeEnvio')
assert.equal(falhaDefinitiva({ status: 'Falhou', erro: INCERTA }), false, 'incerta não é definitiva')
assert.equal(falhaDefinitiva({ status: 'Falhou', erro: PERMANENTE }), true)
assert.equal(falhaDefinitiva({ status: 'Falhou', erro: TEMPORARIA }), true)
assert.equal(falhaDefinitiva({ status: 'Pendente' }), false)
assert.equal(falhaDefinitiva({ status: 'Enviada' }), false)
assert.equal(falhaDefinitiva(null), false)
console.log('ok predicado: só Falhou fora do tempo esgotado é definitiva')

assert.equal(await repetir(porConversa, 'Falhou', INCERTA), true, 'incerta mantém a tentativa (conversa)')
api.prepararOutroModelo('c1')
assert.equal(await repetir(porConversa, 'Pendente'), true, 'pendente mantém a tentativa (conversa)')
api.prepararOutroModelo('c1')
assert.equal(await repetir(porConversa, 'Falhou', PERMANENTE), false, 'falha permanente limpa a tentativa (conversa)')
assert.equal(await repetir(porConversa, 'Falhou', TEMPORARIA), false, 'falha temporária limpa a tentativa (conversa)')
console.log('ok conversa: incerta e pendente mantêm; definitiva limpa')

assert.equal(await repetirCliente('Falhou', INCERTA), true, 'incerta mantém a tentativa (cliente)')
clienteApi.prepararOutroModeloDoCliente('cli1')
assert.equal(await repetirCliente('Pendente'), true, 'pendente mantém a tentativa (cliente)')
clienteApi.prepararOutroModeloDoCliente('cli1')
assert.equal(await repetirCliente('Falhou', PERMANENTE), false, 'falha permanente limpa a tentativa (cliente)')
assert.equal(await repetirCliente('Falhou', TEMPORARIA), false, 'falha temporária limpa a tentativa (cliente)')
console.log('ok Chamar no WhatsApp: incerta e pendente mantêm; definitiva limpa')
