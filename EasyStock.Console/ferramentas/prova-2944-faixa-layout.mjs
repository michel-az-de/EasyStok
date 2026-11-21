// #2944 E1-03 (faixa de itens e total, quantidade no cardápio), E1-04 (quebra, linha editável, rodapé) e E1-08
// (checkbox, small, --b-2).
//   node ferramentas/prova-2944-faixa-layout.mjs
import { assert, caso, fim, fonte } from './prova-2944-base.mjs'

const j = await import('../src/aplicacao/janelaDoPedido.js')
const jsx = fonte('app/NovoPedidoJanela.jsx')
const css = fonte('app/novoPedidoJanela.module.css')

await caso('03a. textoDaFaixa conta itens e mostra o total', () => {
  assert.equal(typeof j.textoDaFaixa, 'function', 'textoDaFaixa ainda não existe')
  assert.equal(j.textoDaFaixa([{ qtd: 2 }], 'R$ 10,00'), '2 itens · R$ 10,00')
  assert.equal(j.textoDaFaixa([{ qtd: 1 }], 'R$ 5,00'), '1 item · R$ 5,00')
  assert.equal(j.textoDaFaixa([], 'R$ 0,00'), 'Nenhum item ainda')
})
await caso('03b. faixa com aria-live em todos os passos e quantidade no cardápio', () => {
  assert.match(jsx, /<p[^>]*aria-live="polite"[^>]*>\{textoDaFaixa\(/)
  assert.match(jsx, /qtdDe\(item\.sku\)/)
})
await caso('04a. o CSS quebra em 759px, não em 640px', () => {
  assert.doesNotMatch(css, /640px/)
  assert.match(css, /max-width: 759px/)
})
await caso('04b. linha editável quebra e erro/aviso ocupam a linha do rodapé', () => {
  assert.match(css, /\.lista li \{[^}]*flex-wrap: wrap/)
  assert.match(css, /\.obs \{[^}]*flex: 1 1 100%/)
  assert.match(css, /\.erro, \.aviso \{[^}]*flex: 1 0 100%/)
})
await caso('08a. sem 2px literal e checkbox de 20px com accent-color', () => {
  assert.doesNotMatch(css, /\b2px\b/)
  assert.match(css, /\.enviar input \{[^}]*width: 20px[^}]*height: 20px[^}]*accent-color/)
})
await caso('08b. small da lista usa token de micro e tinta-2', () => {
  assert.match(css, /\.lista small \{[^}]*var\(--t-micro\)[^}]*var\(--tinta-2\)/)
})
fim()
