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
    if (url.endsWith('/infra/fonteDados.js')) return {
      format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n",
    }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})

const { comApi } = await import('../src/aplicacao/acoesApi.js')
const { reducer } = await import('../src/aplicacao/reducer.js')
const { acaoDisponivel } = await import('../src/aplicacao/api/naoLigadas.js')
const { precisaDeVoce } = await import('../src/dominio/automatico.js')
const { origemDaPassagem } = await import('../src/dominio/passagem.js')
const { permissaoDeEscrita } = await import('../src/dominio/janela.js')
const { situacaoConversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const agora = Date.parse('2026-10-10T15:00:00Z')
const original = {
  id: 'c1', estado: 'Encerrado', canal: 'WhatsApp', responsavel: 'Antigo atendente',
  janelaExpiraEm: '2026-10-09T15:00:00Z', cliente: { notas: [], tags: [] },
  mensagens: [{ id: 'm1', dir: 'in', texto: 'Oi', em: '2026-10-08T15:00:00Z' }],
  pedido: { id: 'pedido-preservado', status: 'cancelado' },
}
const resposta = (data, status = 200) => new Response(JSON.stringify({ data }), { status, headers: { 'Content-Type': 'application/json' } })
const resumo = { id: 'c1', situacao: 'Assumida', assumidaPorUsuarioId: null, naoLidas: 0, motivoEscalada: 'Conversa reaberta para atendimento.' }
function montar() {
  let estado = { conversas: [structuredClone(original)], automaticoPausado: {}, sincronizacao: {} }
  const despachos = []
  const api = comApi({}, {
    despachar: (a) => { despachos.push(a); estado = reducer(estado, a) },
    agoraRef: { current: agora }, estadoRef: { current: estado },
  })
  return { api, despachos, estado: () => estado }
}

assert.equal(acaoDisponivel('reabrir', { fonteApi: true }), true)
{
  const { api, despachos, estado } = montar()
  let confirmar
  const chamadas = []
  globalThis.fetch = async (url, options) => {
    chamadas.push([url, options.method])
    return new Promise((resolve) => { confirmar = resolve })
  }
  const pedido = api.reabrir('c1')
  await new Promise((resolve) => setTimeout(resolve, 0))
  assert.equal(despachos.length, 0, 'sem confirmação, nada muda no navegador')
  assert.equal(estado().conversas[0].estado, 'Encerrado')
  confirmar(resposta(resumo))
  await pedido
  const conversa = estado().conversas[0]
  assert.deepEqual(chamadas, [['/api/atendimento/conversas/c1/reabrir', 'POST']])
  assert.equal(conversa.estado, 'Em atendimento')
  assert.equal(conversa.responsavel, null, 'não conserva o atendente de antes do encerramento')
  assert.equal(precisaDeVoce(conversa, agora, true), true)
  assert.equal(origemDaPassagem(conversa.passagem).curto, resumo.motivoEscalada, 'reabertura humana não atribui falha ao automático')
  assert.equal(conversa.janelaExpiraEm, original.janelaExpiraEm)
  assert.equal(permissaoDeEscrita(conversa, agora).pode, false, 'a janela vencida permanece vencida')
  assert.deepEqual(conversa.pedido, original.pedido)
  assert.deepEqual(conversa.mensagens, original.mensagens)
  console.log('ok confirmação real, fila humana, histórico, pedido e janela preservados')
}
for (const status of [403, 409, 500, 'resposta perdida']) {
  const { api, despachos, estado } = montar()
  globalThis.fetch = async () => {
    if (typeof status === 'string') throw new TypeError('Falha de rede')
    return resposta(null, status)
  }
  await api.reabrir('c1')
  assert.deepEqual(estado().conversas[0], original)
  assert.equal(despachos.length, 1)
  assert.equal(despachos[0].tipo, 'aviso-api')
  console.log(`ok ${status}: aviso sem fingir reabertura`)
}
{
  const situacao = situacaoConversaDaApi({ ...resumo, assumidaPorUsuarioId: 'u1', motivoEscalada: null }, { id: 'u1', nome: 'Atendente' })
  assert.equal(situacao.responsavel, 'Atendente')
  assert.equal(situacao.passagem, null, 'repetição respeita quem já assumiu no servidor')
  assert.equal(situacao.pausaApi, 'assumiu')
  console.log('ok repetição respeita o estado e o responsável do servidor')
}
