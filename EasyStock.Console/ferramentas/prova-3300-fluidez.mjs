/* eslint-disable no-console */
// Prova da #3300 (F7, F8) por leitura do código. Medidas de navegador na PR.
import { assert, caso, fim, fonte } from './prova-2944-base.mjs'

const painel = fonte('features/caixa-de-entrada/PainelConversas.jsx')
const caixa = fonte('features/caixa-de-entrada/caixa.module.css')
const thread = fonte('features/atendimento/Thread.jsx')
const balao = fonte('features/atendimento/Balao.jsx')
const css = fonte('features/atendimento/atendimento.module.css')
const popover = fonte('componentes/Popover.jsx')
const seletor = fonte('features/atendimento/seletorRespostas.module.css')

await caso('F7: a lista de conversas é uma lista para o leitor de tela', () => {
  assert.match(painel, /role="list"/)
  assert.match(painel, /role="listitem"/)
})
await caso('F8: só o balão novo anima; abrir a conversa não anima o fio inteiro', () => {
  assert.doesNotMatch(css, /\.balao \{[^}]*animation: entradaBalao/)
  assert.match(css, /\.balao\.novo \{[^}]*animation: entradaBalao/)
  assert.match(thread, /novo=\{novo\(l\.mensagem\)\}/)
  assert.match(thread, /emMs\(m\) > base\.ultima/)
  assert.match(balao, /novo \? css\.novo/)
})
await caso('P3: Tab para fora do popover de diálogo fecha o popover', () => {
  assert.match(popover, /papel === 'dialog'[\s\S]{0,200}relatedTarget/)
})
await caso('P3: sombra de rolagem do seletor pelo token, sem preto fixo', () => {
  assert.doesNotMatch(seletor, /rgb\(0 0 0 \/ \.16\)/)
})
fim()
