/* eslint-disable no-console */
// Prova da issue #3339, item 9 (R2F-05): "Enviar e assumir" mostra a última fala do Assistente.
// Reprodução: com o Assistente falando por último, a confirmação não trazia a fala (texto fixo, sem a fala).
// Esperado: a confirmação traz a fala do Assistente quando ela é a última da conversa; sem fala, o texto de antes.
//
//   node ferramentas/prova-3339-fala-do-assistente.mjs

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

const { descricaoDaConfirmacao } = await import('../src/features/atendimento/confirmacaoDeEnvioAssumindo.js')

const agora = new Date('2026-11-25T10:00:00-03:00').getTime()
const iso = (m) => new Date(agora - m * 60_000).toISOString()
const base = {
  id: 'c1', nome: 'Maria Souza', canal: 'WhatsApp', estado: 'Aberto', responsavel: null, passagem: null,
  cliente: { tags: [], notas: [] }, pedido: null,
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

const falaDoAssistente = 'Seu pedido sai às 12h. Quer adicionar uma bebida?'

caso('o Assistente falou por último: a confirmação traz a fala, para a atendente ver antes de enviar', () => {
  const conversa = {
    ...base,
    mensagens: [
      { id: 'm1', dir: 'in', texto: 'Oi, a entrega é que horas?', em: iso(5) },
      { id: 'm2', dir: 'out', automatica: true, texto: falaDoAssistente, em: iso(4) },
    ],
  }
  const descricao = descricaoDaConfirmacao(conversa)
  assert.ok(descricao.includes(falaDoAssistente), 'a fala do Assistente aparece na confirmação')
  assert.match(descricao, /Maria/, 'o texto de antes continua')
})

caso('a última fala é da atendente: a confirmação não traz fala do Assistente', () => {
  const conversa = {
    ...base,
    mensagens: [
      { id: 'm1', dir: 'in', texto: 'Oi', em: iso(5) },
      { id: 'm2', dir: 'out', texto: 'Oi, já te respondo', em: iso(4) },
    ],
  }
  assert.doesNotMatch(descricaoDaConfirmacao(conversa), /Oi, já te respondo|Seu pedido/)
})

caso('aviso da esteira não é fala do Assistente e não entra na confirmação', () => {
  const conversa = {
    ...base,
    mensagens: [
      { id: 'm1', dir: 'in', texto: 'Oi', em: iso(5) },
      { id: 'm2', dir: 'out', automatica: true, regra: 'esteira-saiu', texto: 'Pedido saiu para entrega', em: iso(4) },
    ],
  }
  assert.doesNotMatch(descricaoDaConfirmacao(conversa), /Pedido saiu/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
