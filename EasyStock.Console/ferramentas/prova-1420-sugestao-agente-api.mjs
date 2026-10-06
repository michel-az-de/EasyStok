// Prova da #1420: no modo API, "Sugerir" do painel do agente chama o EasyStok
// (POST .../conversas/{id}/sugestao), põe o texto no painel e não envia nada ao cliente.
// Agente desligado no servidor (503) vira o erro do painel, com a mensagem da API.
//
//   node ferramentas/prova-1420-sugestao-agente-api.mjs
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
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})

const acao = await import('../src/aplicacao/acoes.js')
const { criarAcoes } = await import('../src/aplicacao/criarAcoes.js')
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const { NAO_LIGADAS } = await import('../src/aplicacao/api/naoLigadas.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')

globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
const chamadas = []
let resposta = () => new Response(JSON.stringify({ data: { texto: 'Oi Maria! Hoje tem bolo de cenoura.', tokens: 250, latenciaMs: 900 } }))
globalThis.fetch = async (url, opcoes = {}) => {
  chamadas.push({ url, metodo: opcoes.method ?? 'GET' })
  return resposta()
}

const estadoRef = { current: { conversas: [{ id: 'c1', canal: 'WhatsApp', mensagens: [], cliente: { notas: [], tags: [] } }] } }
const despachos = []
const despachar = (a) => despachos.push(a)
const agoraRef = { current: Date.now() }
const locais = criarAcoes({
  despachar, agoraRef, estadoRef, pendentes: { current: [] },
  consultarAgente: async () => { throw new Error('não pode cair no agente local') },
  perguntarAssistente: async () => '', pedirNotificacaoDoNavegador: async () => {},
})
const api = comApi(locais, { despachar, agoraRef, estadoRef })

assert.equal('consultarAgente' in NAO_LIGADAS, false, 'consultarAgente saiu das não ligadas')

await api.consultarAgente('c1')
assert.deepEqual(chamadas, [{ url: '/api/atendimento/conversas/c1/sugestao', metodo: 'POST' }], 'uma chamada, só a sugestão')
assert.equal(despachos.some((a) => a.tipo === acao.AVISO_API), false, 'não avisa "ainda não ligado"')
assert.deepEqual(despachos.map((a) => a.tipo), [acao.AGENTE_PEDINDO, acao.AGENTE_RESPONDEU])
const { sugestao } = despachos[1]
assert.equal(sugestao.texto, 'Oi Maria! Hoje tem bolo de cenoura.')
assert.equal(sugestao.acao, 'propor')
assert.ok(sugestao.intencao.rotulo, 'o painel lê o rótulo da intenção')

const estado = despachos.reduce(reducer, estadoInicial({ conversas: [], catalogo: { cardapio: [], janelas: [], canais: [] }, regras: [] }))
assert.equal(estado.agente.estado, 'pronto')
assert.equal(estado.agente.conversaId, 'c1')
assert.equal(estado.agente.sugestao.texto, 'Oi Maria! Hoje tem bolo de cenoura.', 'o painel mostra a sugestão')

despachos.length = 0
resposta = () => new Response(JSON.stringify({ error: { code: 'AGENTE_INDISPONIVEL',
  message: 'Agente indisponível: configure Anthropic:Enabled=true e Anthropic:ApiKey no servidor.' } }), { status: 503 })
await api.consultarAgente('c1')
assert.deepEqual(despachos.map((a) => a.tipo), [acao.AGENTE_PEDINDO, acao.AGENTE_FALHOU])
assert.match(despachos[1].erro, /Anthropic:Enabled/)

console.log('Sugestão do agente no modo API: chamada, painel, sem envio e 503 verificados.')
