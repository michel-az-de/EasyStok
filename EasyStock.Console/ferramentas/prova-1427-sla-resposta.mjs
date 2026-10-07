/* eslint-disable no-console */
// Prova da issue #1427: SLA de primeira resposta configurável por loja, cartão
// que pisca quando estoura e contagem que pausa fora do expediente. Importa as
// MESMAS funções que a tela usa (dominio/funcionamento.js, automatico.js,
// conversa.js, a tradução da API e a regra de classes do cartão):
//
//   node ferramentas/prova-1427-sla-resposta.mjs
//
// Mesmo gancho de resolução de `prova-contadores-canal.mjs`.

process.env.TZ = 'America/Sao_Paulo'

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

registerHooks({
  resolve(especificador, contexto, proximo) {
    try {
      return proximo(especificador, contexto)
    } catch (erro) {
      if (especificador.startsWith('.') && !/\.m?jsx?$/.test(especificador)) {
        return proximo(especificador + '.js', contexto)
      }
      throw erro
    }
  },
})

const { minutosAbertos, FUNCIONAMENTO_PADRAO } = await import('../src/dominio/funcionamento.js')
const {
  SLA_RESPOSTA_PADRAO, MINUTOS_DE_ESPERA, motivoDePrecisar, respostaAtrasada, slaDaConversa,
} = await import('../src/dominio/automatico.js')
const { ordenarBalcao } = await import('../src/dominio/conversa.js')
const { conversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { classesDoCartao } = await import('../src/features/caixa-de-entrada/classesDoCartao.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const AGORA = Date.parse('2026-10-07T12:00:00-03:00')
const minutosAtras = (m, base = AGORA) => new Date(base - m * 60000).toISOString()
const EXPEDIENTE = { funcionamento: FUNCIONAMENTO_PADRAO, lojaAberta: null } // 8h às 22h todo dia

const conversa = (id, minutos, extra = {}) => ({
  id, nome: id, estado: 'Em atendimento', bloqueio: null, pedido: null, ultimaEm: minutosAtras(minutos),
  mensagens: [{ id: `${id}-1`, dir: 'in', texto: 'Tem ravióli hoje?', em: minutosAtras(minutos) }],
  ...extra,
})

// --- 1. Relógio da loja ------------------------------------------------------
confere('minutos abertos pulam a noite fechada (21h58 até 8h03 = 5 min)', () => {
  const inicio = Date.parse('2026-10-06T21:58:00-03:00')
  const fim = Date.parse('2026-10-07T08:03:00-03:00')
  assert.equal(minutosAbertos(inicio, fim, EXPEDIENTE), 5)
})

confere('loja aberta na mão conta o relógio inteiro; fechada na mão conta zero', () => {
  const inicio = AGORA - 30 * 60000
  assert.equal(minutosAbertos(inicio, AGORA, { ...EXPEDIENTE, lojaAberta: true }), 30)
  assert.equal(minutosAbertos(inicio, AGORA, { ...EXPEDIENTE, lojaAberta: false }), 0)
})

confere('semana inteira fechada nunca conta e não trava', () => {
  const fechada = Object.fromEntries(
    Object.keys(FUNCIONAMENTO_PADRAO).map((dia) => [dia, { abre: null, fecha: null, fechado: true }]),
  )
  assert.equal(minutosAbertos(AGORA - 3 * 86400000, AGORA, { funcionamento: fechada }), 0)
})

confere('a conta para assim que passa do limite (quem pergunta só quer saber se estourou)', () => {
  const minutos = minutosAbertos(AGORA - 7 * 86400000, AGORA, EXPEDIENTE, 6)
  assert.ok(minutos > 6 && minutos < 7 * 24 * 60, `parou fora do esperado: ${minutos}`)
})

// --- 2. SLA da loja no lugar da constante ------------------------------------
confere('sem SLA na conversa vale o padrão de 5 min (o nome antigo segue igual)', () => {
  assert.equal(SLA_RESPOSTA_PADRAO, 5)
  assert.equal(MINUTOS_DE_ESPERA, 5)
  assert.equal(slaDaConversa({}), 5)
  assert.equal(motivoDePrecisar(conversa('a', 5), AGORA), null)
  assert.equal(motivoDePrecisar(conversa('a', 6), AGORA)?.chave, 'esperando')
})

confere('o SLA configurado na loja decide quando a conversa precisa de você', () => {
  assert.equal(motivoDePrecisar(conversa('b', 9, { slaMinutos: 10 }), AGORA), null)
  const motivo = motivoDePrecisar(conversa('b', 11, { slaMinutos: 10 }), AGORA)
  assert.equal(motivo?.chave, 'esperando')
  assert.equal(motivo?.estourado, true)
  assert.equal(respostaAtrasada(conversa('b', 11, { slaMinutos: 10 }), AGORA), true)
})

// --- 3. Pausa fora do expediente ----------------------------------------------
confere('fora do expediente o SLA pausa: 21h58 estoura às 8h04, não às 22h04', () => {
  const as2158 = Date.parse('2026-10-06T21:58:00-03:00')
  const c = {
    ...conversa('noite', 0), slaMinutos: 5,
    mensagens: [{ id: 'n1', dir: 'in', texto: 'Amanhã tem nhoque?', em: new Date(as2158).toISOString() }],
  }
  assert.equal(respostaAtrasada(c, Date.parse('2026-10-06T23:30:00-03:00'), true, EXPEDIENTE), false)
  assert.equal(respostaAtrasada(c, Date.parse('2026-10-07T08:02:00-03:00'), true, EXPEDIENTE), false)
  assert.equal(respostaAtrasada(c, Date.parse('2026-10-07T08:04:00-03:00'), true, EXPEDIENTE), true)
  // Sem expediente (quem ainda não passa), relógio inteiro como antes.
  assert.equal(respostaAtrasada(c, Date.parse('2026-10-06T23:30:00-03:00'), true), true)
})

// --- 4. O que a API manda ----------------------------------------------------
confere('sem mensagens carregadas, vale o resumo da API (aguarda resposta e última entrada)', () => {
  const resumo = { ...conversa('r', 0), mensagens: [], aguardaResposta: true, ultimaEntradaEm: minutosAtras(8) }
  assert.equal(respostaAtrasada(resumo, AGORA, true), true)
  assert.equal(respostaAtrasada({ ...resumo, aguardaResposta: false }, AGORA, true), false)
})

confere('a tradução da API leva o SLA, a última entrada e calcula `atrasada`', () => {
  const resumo = {
    id: 'api-1', canal: 'WhatsApp', contatoIdExterno: '5511999990001', contatoNome: 'Fulana', clienteId: null,
    situacao: 'Assumida', naoLidas: 1, ultimaMensagemEm: minutosAtras(12), ultimaMensagemTexto: 'oi?',
    pedidoEmAndamentoId: null, assumidaPorUsuarioId: 'u1', dentroDaJanela: true, motivoEscalada: null,
    ultimaMensagemEntradaEm: minutosAtras(12), aguardaResposta: true, slaRespostaMinutos: 10,
  }
  const mensagens = [{
    id: 'm1', direcao: 'Entrada', autor: 'Cliente', tipoConteudo: 'Texto', texto: 'oi?',
    status: 'Entregue', enviadaEm: minutosAtras(12),
  }]
  const traduzida = conversaDaApi(resumo, mensagens, { id: 'u1', nome: 'Thati' }, AGORA)
  assert.equal(traduzida.slaMinutos, 10)
  assert.equal(traduzida.aguardaResposta, true)
  assert.equal(new Date(traduzida.ultimaEntradaEm).getTime(), Date.parse(minutosAtras(12)))
  assert.equal(traduzida.atrasada, true)
  assert.equal(conversaDaApi({ ...resumo, slaRespostaMinutos: 15 }, mensagens, { id: 'u1' }, AGORA).atrasada, false)
})

// --- 5. Topo de "Precisa de você" --------------------------------------------
confere('estourado vai para o topo de "Precisa de você", em qualquer ordem', () => {
  const recente = conversa('recente', 7, { slaMinutos: 30, passagem: { motivo: 'x', assumida: false } })
  const estourada = conversa('estourada', 6, { slaMinutos: 5 })
  const antiga = conversa('antiga', 20, { slaMinutos: 30, passagem: { motivo: 'y', assumida: false } })
  const lista = [recente, antiga, estourada]
  const ids = (l) => l.map((c) => c.id)
  assert.deepEqual(ids(ordenarBalcao(lista, AGORA, { aba: 'precisa' })), ['estourada', 'antiga', 'recente'])
  assert.deepEqual(
    ids(ordenarBalcao(lista, AGORA, { aba: 'precisa', ordenacao: 'recentes' })), ['estourada', 'recente', 'antiga'],
  )
  // Fora da aba, a ordem escolhida manda sozinha.
  assert.deepEqual(
    ids(ordenarBalcao([antiga, estourada, recente], AGORA, { aba: 'todas', ordenacao: 'recentes' })),
    ['estourada', 'recente', 'antiga'],
  )
  const semPrazo = conversa('sem-prazo', 1)
  assert.deepEqual(
    ids(ordenarBalcao([antiga, semPrazo], AGORA, { aba: 'todas', ordenacao: 'recentes' })), ['sem-prazo', 'antiga'],
  )
})

confere('passagem com o cliente esperando além do prazo também pisca', () => {
  const c = conversa('passou', 40, { slaMinutos: 10, passagem: { motivo: 'restrição', assumida: false } })
  assert.equal(motivoDePrecisar(c, AGORA)?.chave, 'passagem')
  assert.equal(respostaAtrasada(c, AGORA, true), true)
})

confere('encerrada ou bloqueada nunca pisca', () => {
  assert.equal(respostaAtrasada(conversa('e', 60, { estado: 'Encerrado' }), AGORA), false)
  assert.equal(respostaAtrasada(conversa('b', 60, { bloqueio: { em: minutosAtras(1) } }), AGORA), false)
})

// --- 6. Cartão -----------------------------------------------------------------
confere('o cartão ganha a classe de pisca só com o SLA estourado', () => {
  const css = { cartao: 'c', precisa: 'p', atrasada: 'a', slaEstourado: 's' }
  assert.equal(classesDoCartao(css, { motivo: { tom: 'perigo' }, slaEstourado: true }), 'c a s')
  assert.equal(classesDoCartao(css, { motivo: { tom: 'aviso' }, slaEstourado: true }), 'c p s')
  assert.equal(classesDoCartao(css, { motivo: { tom: 'perigo' } }), 'c a')
  assert.equal(classesDoCartao(css), 'c')
})

confere('com movimento reduzido não pisca: fica só o destaque de cor', () => {
  const folha = readFileSync(new URL('../src/features/caixa-de-entrada/caixa.module.css', import.meta.url), 'utf8')
  assert.match(folha, /\.cartao\.slaEstourado\s*\{[^}]*animation:[^}]*piscaSla[^}]*infinite/)
  const reduzido = folha.slice(folha.indexOf('@media (prefers-reduced-motion: reduce)'))
  assert.match(reduzido, /\.cartao\.slaEstourado\s*\{\s*animation:\s*none;\s*background:\s*var\(--parado-pastel\)/)
})

console.log(`\n${passou} verificações passaram.`)
