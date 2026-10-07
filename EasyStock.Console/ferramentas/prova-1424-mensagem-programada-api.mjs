// Prova da #1424: no modo API, "Programar" do compositor fala com a API de mensagens
// programadas que já existe (S39) e não guarda nada no navegador.
//   1. agendar texto: POST com clienteId, conversaId, canal e finalidade como a API lê (enum em
//      string), horário em ISO UTC e só o texto (texto e modelo nunca juntos);
//   2. agendar por modelo: nome, idioma pt_BR e parâmetros, sem texto;
//   3. listar: GET por cliente e só as desta conversa (ou do mesmo canal sem conversa);
//   4. cancelar: DELETE pelo id;
//   5. erro 400 da API chega com a mensagem dela; conversa sem cliente não chama a API;
//   6. demonstração não finge: as três recusam com o motivo;
//   7. o balão da API ganha `programada` (selo) e a tradução da linha da lista;
//   8. regras da modal (horário local -> UTC, o que falta para agendar).
//
//   node ferramentas/prova-1424-mensagem-programada-api.mjs
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

const { criarAcoes } = await import('../src/aplicacao/criarAcoes.js')
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const { mensagemDaApi, programadaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { SEM_CLIENTE } = await import('../src/aplicacao/api/mensagensProgramadas.js')
const regras = await import('../src/dominio/mensagemProgramada.js')

globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
globalThis.window = { dispatchEvent: () => {}, location: { origin: 'http://x', pathname: '/' } }

const CLIENTE = '11111111-1111-1111-1111-111111111111'
const resultado = (extra = {}) => ({
  id: 'p1', clienteId: CLIENTE, conversaId: 'c1', canal: 'WhatsApp', finalidade: 'Transacional', texto: 'Bom dia!',
  modelo: null, agendadaPara: '2026-10-08T12:00:00', situacao: 'Agendada', tentativas: 0, erro: null, enviadaEm: null, ...extra,
})
const chamadas = []
let resposta = () => new Response(JSON.stringify({ data: resultado() }))
globalThis.fetch = async (url, opcoes = {}) => {
  chamadas.push({ url, metodo: opcoes.method ?? 'GET', corpo: opcoes.body ? JSON.parse(opcoes.body) : undefined })
  return resposta(url, opcoes)
}

const estadoRef = {
  current: {
    conversas: [
      { id: 'c1', canal: 'WhatsApp', clienteId: CLIENTE, mensagens: [], cliente: { notas: [], tags: [] } },
      { id: 'c2', canal: 'Instagram', clienteId: null, mensagens: [], cliente: { notas: [], tags: [] } },
    ],
  },
}
const despachos = []
const montar = () => {
  const locais = criarAcoes({
    despachar: (a) => despachos.push(a), agoraRef: { current: Date.now() }, estadoRef, pendentes: { current: [] },
    consultarAgente: async () => {}, perguntarAssistente: async () => '', pedirNotificacaoDoNavegador: async () => {},
  })
  return { locais, api: comApi(locais, { despachar: (a) => despachos.push(a), agoraRef: { current: Date.now() }, estadoRef }) }
}
const { locais, api } = montar()

// 1. Texto.
const p = await api.programarMensagem('c1', {
  finalidade: 'Transacional', texto: '  Bom dia!  ', agendadaPara: '2026-10-08T12:00:00.000Z',
})
assert.deepEqual(chamadas.at(-1), {
  url: '/api/atendimento/mensagens-programadas',
  metodo: 'POST',
  corpo: {
    clienteId: CLIENTE, conversaId: 'c1', canal: 'WhatsApp', finalidade: 'Transacional',
    texto: 'Bom dia!', agendadaPara: '2026-10-08T12:00:00.000Z',
  },
})
assert.equal(p.situacao, 'Agendada')
assert.equal(p.agendadaPara, '2026-10-08T12:00:00Z', 'DateTime da API vira UTC explícito')
assert.equal(despachos.length, 0, 'programar não mexe no estado do navegador')

// 2. Modelo.
await api.programarMensagem('c1', {
  finalidade: 'Marketing', texto: 'ignorado', agendadaPara: '2026-10-09T12:00:00.000Z',
  modelo: { nome: ' lembrete_pedido ', idioma: '', parametros: ['Maria', 'sexta'] },
})
const corpoModelo = chamadas.at(-1).corpo
assert.equal('texto' in corpoModelo, false, 'texto e modelo nunca juntos')
assert.deepEqual(corpoModelo.modelo, { nome: 'lembrete_pedido', idioma: 'pt_BR', parametros: ['Maria', 'sexta'] })
assert.equal(corpoModelo.finalidade, 'Marketing')

// 3. Lista desta conversa.
resposta = () => new Response(JSON.stringify({
  data: [
    resultado(),
    resultado({ id: 'p2', conversaId: 'outra' }),
    resultado({ id: 'p3', conversaId: null }),
    resultado({ id: 'p4', conversaId: null, canal: 'Sms' }),
  ],
}))
const lista = await api.listarProgramadas('c1')
assert.equal(chamadas.at(-1).url, `/api/atendimento/mensagens-programadas?clienteId=${CLIENTE}`)
assert.deepEqual(lista.map((x) => x.id), ['p1', 'p3'], 'só as da conversa e as do canal sem conversa')

// 4. Cancelar.
resposta = () => new Response(JSON.stringify({ data: resultado({ situacao: 'Cancelada' }) }))
const cancelada = await api.cancelarProgramada('p1')
assert.deepEqual(chamadas.at(-1), { url: '/api/atendimento/mensagens-programadas/p1', metodo: 'DELETE', corpo: undefined })
assert.equal(cancelada.situacao, 'Cancelada')

// 5. Erro da API e conversa sem cliente.
const JANELA = 'Fora da janela de 24 h do WhatsApp no horário do envio: use um modelo aprovado.'
resposta = () => new Response(JSON.stringify({ error: { code: 'VALIDATION_ERROR', message: JANELA } }), { status: 400 })
await assert.rejects(api.programarMensagem('c1', { finalidade: 'Transacional', texto: 'oi', agendadaPara: '2026-10-08T12:00:00.000Z' }),
  (e) => e.message === JANELA && e.status === 400)
const antes = chamadas.length
await assert.rejects(api.programarMensagem('c2', { finalidade: 'Transacional', texto: 'oi', agendadaPara: '2026-10-08T12:00:00.000Z' }), { message: SEM_CLIENTE })
await assert.rejects(api.listarProgramadas('c2'), { message: SEM_CLIENTE })
assert.equal(chamadas.length, antes, 'sem cliente não chama a API')

// 6. Demonstração.
for (const nome of ['programarMensagem', 'listarProgramadas', 'cancelarProgramada']) {
  assert.notEqual(api[nome], locais[nome], `${nome} ligada pela API`)
  await assert.rejects(locais[nome]('c1', {}), /só sai pelo EasyStok/)
}

// 7. Tradução.
const saida = { id: 'm1', direcao: 'Saida', status: 'Enviada', autor: 'Dona', enviadaEm: '2026-10-08T12:00:00', tipoConteudo: 'Texto', texto: 'Bom dia!' }
assert.equal(mensagemDaApi({ ...saida, programada: true }).programada, true, 'selo "programada" no balão')
assert.equal('programada' in mensagemDaApi({ ...saida, programada: false }), false)
assert.equal(programadaDaApi(resultado({ canal: 'ChatSite' })).canal, 'Chat do site')

// 8. Regras da modal.
const local = '2026-10-08T09:30'
assert.equal(regras.horarioLocalParaUtc(local), new Date(local).toISOString())
assert.equal(regras.horarioLocalParaUtc(''), null)
assert.match(regras.horarioLocalPadrao(Date.parse('2026-10-07T10:00:30Z')), /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/)
const agora = Date.parse('2026-10-07T12:00:00Z')
assert.match(regras.faltaParaProgramar({ agendadaPara: '2026-10-07T11:00:00Z', agoraMs: agora, texto: 'oi' }), /depois de agora/)
assert.match(regras.faltaParaProgramar({ agendadaPara: '2026-10-07T13:00:00Z', agoraMs: agora, texto: ' ' }), /Escreva/)
assert.match(regras.faltaParaProgramar({ agendadaPara: '2026-10-07T13:00:00Z', agoraMs: agora, usarModelo: true }), /nome do modelo/)
assert.equal(regras.faltaParaProgramar({ agendadaPara: '2026-10-07T13:00:00Z', agoraMs: agora, texto: 'oi' }), null)
assert.deepEqual(regras.parametrosDoTexto(' Maria \n\n sexta '), ['Maria', 'sexta'])
assert.equal(regras.podeCancelar({ situacao: 'Agendada' }), true)
assert.equal(regras.podeCancelar({ situacao: 'Enviando' }), false)

console.log('Mensagem programada no modo API: agendar (texto e modelo), listar, cancelar, erro da API, sem cliente, demonstração e selo verificados.')
