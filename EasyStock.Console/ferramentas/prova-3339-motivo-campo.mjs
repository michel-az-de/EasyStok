/* eslint-disable no-console */
// Prova da issue #3339, item 3 (R1D-08, R1D-14): o motivo que fecha o campo e a barra diz o porquê.
//   a janela vencida com o atendente conduzindo: a barra não promete resposta e diz que a janela fechou;
//   b o campo fechado diz o motivo (janela, conversa concluída), não só "Mensagem indisponível";
//   c sem motivo de janela, a barra fica como estava (motivo da fila ou nada).
//
//   node ferramentas/prova-3339-motivo-campo.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})

const { permissaoDeEscrita, motivoDoCampoFechado } = await import('../src/dominio/janela.js')
const { resumoDoModo, motivoDaBarra } = await import('../src/dominio/resumoDoModo.js')

const agora = new Date('2026-11-25T10:00:00-03:00').getTime()
const iso = (m) => new Date(agora - m * 60_000).toISOString()
const whatsapp = { nome: 'WhatsApp', temJanela: true, horasJanela: 24, aceitaModelo: true, desconhecido: false }

// Cliente falou há 30 h, a janela de 24 h fechou; eu respondi por último há 20 min e assumi a conversa.
const janelaVencida = {
  id: 'c1', nome: 'Cliente', canal: 'WhatsApp', estado: 'Aberto', responsavel: 'Felipe', passagem: null,
  cliente: { tags: [], notas: [] }, pedido: null,
  mensagens: [
    { id: 'm1', dir: 'in', texto: 'Oi, tudo bem?', em: iso(30 * 60) },
    { id: 'm2', dir: 'out', texto: 'Oi! Posso ajudar?', em: iso(20) },
  ],
  janelaExpiraEm: iso(6 * 60),
}

let passou = 0
const falhas = []
const caso = (descricao, fn) => {
  try {
    fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log('FALHA ' + descricao + '\n      ' + (erro.message ?? erro).split('\n')[0])
  }
}

caso('a: janela vencida sem texto livre tem motivo de janela na permissão', () => {
  const { pode, motivo } = permissaoDeEscrita(janelaVencida, agora, whatsapp)
  assert.equal(pode, false)
  assert.equal(motivo?.acao, 'janela')
})

caso('a: com o atendente conduzindo e janela vencida, a barra diz que a janela fechou', () => {
  const { motivo } = permissaoDeEscrita(janelaVencida, agora, whatsapp)
  const motivoBarra = motivoDaBarra(null, motivo, true)
  assert.ok(motivoBarra, 'a barra recebe o motivo de janela quando o atendente conduz')
  const r = resumoDoModo(janelaVencida, true, false, { agora, motivo: motivoBarra })
  assert.match(r.linha, /Janela fechada/)
  assert.doesNotMatch(r.linha, /Conclua|devolva/, 'a linha não sugere saída que a janela não dá')
  assert.equal(r.rotulo, 'Você respondendo')
})

caso('b: o campo fechado com janela vencida diz o motivo, não só "indisponível"', () => {
  const { motivo } = permissaoDeEscrita(janelaVencida, agora, whatsapp)
  const texto = motivoDoCampoFechado(motivo)
  assert.match(texto, /^Janela fechada/)
  assert.notEqual(texto, 'Mensagem indisponível nesta conversa')
})

caso('b: conversa concluída diz o motivo no campo', () => {
  const concluida = { ...janelaVencida, estado: 'Encerrado' }
  const { motivo } = permissaoDeEscrita(concluida, agora, whatsapp)
  assert.equal(motivoDoCampoFechado(motivo), 'Conversa concluída.')
})

caso('b: sem motivo cai no texto antigo', () => {
  assert.equal(motivoDoCampoFechado(null), 'Mensagem indisponível nesta conversa')
})

caso('c: janela aberta não cria motivo para a barra', () => {
  const aberta = { ...janelaVencida, janelaExpiraEm: iso(-60) }
  const { motivo } = permissaoDeEscrita(aberta, agora, whatsapp)
  assert.equal(motivo, null)
  assert.equal(motivoDaBarra(null, motivo, true), null)
})

caso('c: o motivo da fila (precisa de você) vence o de janela', () => {
  const fila = { chave: 'esperando', texto: 'Cliente esperando há 2 h', tom: 'perigo' }
  const { motivo } = permissaoDeEscrita(janelaVencida, agora, whatsapp)
  assert.equal(motivoDaBarra(fila, motivo, true), fila)
})

caso('c: conversa ligada (não pausada) não usa o motivo de janela', () => {
  const { motivo } = permissaoDeEscrita(janelaVencida, agora, whatsapp)
  assert.equal(motivoDaBarra(null, motivo, false), null)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
