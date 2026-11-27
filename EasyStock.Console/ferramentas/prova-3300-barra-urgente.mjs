/* eslint-disable no-console */
// Prova da #3300 (F1): a conversa que sobe para "Precisa de você" pelo sinal das mensagens (`sinalUrgente`) não pode
// dizer "Assistente respondendo" na barra. Antes a barra só lia a passagem gravada; o cartão lia as mensagens.
import { assert, caso, fim, fonte } from './prova-2944-base.mjs'

const { resumoDoModo } = await import('../src/dominio/resumoDoModo.js')
const { motivoDePrecisar } = await import('../src/dominio/automatico.js')

const agora = new Date('2026-11-24T10:50:00-03:00').getTime()
const iso = (m) => new Date(agora - m * 60_000).toISOString()
const conversa = (mensagens, extra = {}) => ({
  id: 'x1', nome: 'Renan O.', canal: 'Chat do site', estado: 'Aberto', responsavel: null, passagem: null,
  cliente: { tags: [], notas: [] }, pedido: null, mensagens, ...extra,
})
const restricao = conversa([
  { id: 'm1', dir: 'in', texto: 'Oi! Vocês têm alguma opção vegana? Parei com laticínio e ovo', em: iso(10) },
  { id: 'm2', dir: 'in', texto: 'O ravióli de abóbora leva ovo na massa?', em: iso(5) },
])

await caso('restrição sem passagem: a barra diz "Precisa de você", não "Assistente respondendo"', () => {
  const r = resumoDoModo(restricao, false, false, { agora })
  assert.equal(r.rotulo, 'Precisa de você')
  assert.equal(r.tom, 'aviso')
})

await caso('a linha da barra é o mesmo motivo do cartão da lista', () => {
  const cartao = motivoDePrecisar(restricao, agora, false)
  const r = resumoDoModo(restricao, false, false, { agora })
  assert.ok(cartao?.texto, 'o cartão tem motivo')
  assert.equal(r.linha, cartao.texto)
})

await caso('o motivo da restrição não promete que o Assistente não responde (no modo API ele responde com a ficha)', () => {
  const cartao = motivoDePrecisar(restricao, agora, false)
  assert.doesNotMatch(cartao.texto, /não responde/)
})

await caso('devolução sobe com o próprio texto', () => {
  const c = conversa([{ id: 'm1', dir: 'in', texto: 'O pedido veio errado, quero meu dinheiro de volta', em: iso(3) }])
  const r = resumoDoModo(c, false, false, { agora })
  assert.equal(r.rotulo, 'Precisa de você')
  assert.equal(r.linha, motivoDePrecisar(c, agora, false).texto)
})

await caso('conversa comum continua "Assistente respondendo"', () => {
  const c = conversa([{ id: 'm1', dir: 'in', texto: 'Qual o horário de vocês hoje?', em: iso(3) }])
  assert.equal(resumoDoModo(c, false, false, { agora }).rotulo, 'Assistente respondendo')
})

await caso('mensagem sem texto (resumo da API) não opina', () => {
  const c = conversa([{ id: 'm1', dir: 'in', em: iso(3) }])
  assert.equal(resumoDoModo(c, false, false, { agora }).rotulo, 'Assistente respondendo')
})

await caso('pausado (você respondendo) não muda com o sinal', () => {
  assert.notEqual(resumoDoModo(restricao, true, false, { agora }).rotulo, 'Precisa de você')
})

await caso('sem `agora` o resumo segue como antes (chamador antigo)', () => {
  assert.equal(resumoDoModo(restricao, false, false).rotulo, 'Assistente respondendo')
})

// #3374: com o selo (#3234 C5) a BarraModo lê o resumo por `apresentacaoDoModo`, que repassa `agora` e `motivo`.
await caso('a BarraModo passa `agora` ao resumo', () => {
  assert.match(fonte('features/atendimento/BarraModo.jsx'), /apresentacaoDoModo\([^)]*agora/)
  assert.match(fonte('dominio/resumoDoModo.js'), /export function apresentacaoDoModo[\s\S]*?resumoDoModo\([^)]*agora[^)]*motivo/)
})

await caso('o detalhe (title e leitor de tela) não diz "Assistente respondendo" com o sinal', () => {
  const r = resumoDoModo(restricao, false, false, { agora })
  assert.doesNotMatch(r.detalhe, /Assistente respondendo/)
  assert.match(r.detalhe, /Precisa de você/)
})

await caso('devolução mantém o tom de perigo do cartão', () => {
  const c = conversa([{ id: 'm1', dir: 'in', texto: 'O pedido veio errado, quero meu dinheiro de volta', em: iso(3) }])
  assert.equal(resumoDoModo(c, false, false, { agora }).tom, motivoDePrecisar(c, agora, false).tom)
})

fim()
