// #2944 E1-10: fila do Atendimento (animação só no cartão novo; erro de carga com "Tentar de novo").
//   node ferramentas/prova-2944-fila.mjs
import { assert, caso, fim, fonte } from './prova-2944-base.mjs'

const css = fonte('features/caixa-de-entrada/caixa.module.css')
const painel = fonte('features/caixa-de-entrada/PainelConversas.jsx')
const cartao = fonte('features/caixa-de-entrada/CartaoConversa.jsx')
const ciclo = fonte('aplicacao/useSincronizacaoApi.js')

await caso('10a. a animação de entrada fica só na classe do cartão novo', () => {
  assert.doesNotMatch(css, /\.cartao \{[^}]*animation:/)
  assert.match(css, /\.cartaoNovo \{[^}]*animation: entradaCartao/)
  assert.match(cartao, /cartaoNovo/)
})
await caso('10b. erro de carga oferece "Tentar de novo" e o laço escuta o pedido', () => {
  assert.match(painel, /<VazioEmLinha[^\n]*Não consegui carregar as conversas[^\n]*acao="Tentar de novo"/)
  assert.match(ciclo, /easystok:sincronizar-agora/)
})
fim()
