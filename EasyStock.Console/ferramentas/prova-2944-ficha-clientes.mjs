// #2944 E1-07 (ficha: dt antes de dd, remover foto com confirmação) e E1-09 (Clientes no celular).
//   node ferramentas/prova-2944-ficha-clientes.mjs
import { assert, caso, fim, fonte } from './prova-2944-base.mjs'

const ficha = fonte('features/gestao/cadastros/ResumoDoCliente.jsx')
const css = fonte('features/gestao/cadastros/cadastros.module.css')
const painel = fonte('features/gestao/cadastros/PainelCadastros.jsx')

await caso('07a. no quadro do cliente o dt vem antes do dd no DOM', () => {
  assert.doesNotMatch(ficha, /<dd>\{n\.valor\}<\/dd><dt>/)
  assert.match(ficha, /<dt>\{n\.rotulo\}<\/dt><dd>\{n\.valor\}<\/dd>/)
  assert.match(css, /\.quadroCliente > div \{[^}]*flex-direction: column-reverse/)
})
await caso('07b. Remover foto pede confirmação', () => assert.match(ficha, /Confirmar remoção da foto/))
await caso('09a. botões da linha só ficam em nowrap acima de 700px', () => {
  assert.doesNotMatch(css, /^\.lista \.acoes button \{ white-space: nowrap; \}/m)
  assert.match(css, /@media \(min-width: 701px\) \{[^}]*\.lista \.acoes button \{ white-space: nowrap/)
})
await caso('09b. linha clicável mostra foco por teclado', () => assert.match(css, /\.clicavel:focus-within td/))
await caso('09c. Carregando usa EsqueletoDeLista e Atualizar explica o bloqueio', () => {
  assert.match(painel, /<EsqueletoDeLista\b/)
  assert.match(painel, /<Botao[^\n]*refresh-cw[^\n]*motivo=/)
})
fim()
