/* eslint-disable no-console */
// Prova da #3322: o aviso de mensagens não entregues cabe numa linha na coluna de Conversas. Em produção (25/11)
// "6 mensagens não entregues · Revisar" quebrava em 2 linhas. A largura real é medida na produção após o deploy
// (a demonstração não tem mensagens não entregues); aqui ficam o texto e as regras que impedem a quebra.
import { assert, caso, fim, fonte } from './prova-2944-base.mjs'

const { textoDaFaixaNaoEntregues } = await import('../src/features/caixa-de-entrada/textoNaoEntregues.js')
const jsx = fonte('features/caixa-de-entrada/NaoEntregues.jsx')
const css = fonte('features/caixa-de-entrada/caixa.module.css')
const regra = (seletor) => css.match(new RegExp(`\\.${seletor}\\s*\\{([^}]*)\\}`))?.[1] ?? ''

await caso('texto visível curto, sem "mensagens"', () => {
  assert.equal(textoDaFaixaNaoEntregues({ total: 6 }).visivel, '6 não entregues')
  assert.equal(textoDaFaixaNaoEntregues({ total: 1 }).visivel, '1 não entregue')
})

await caso('o leitor de tela ouve a frase inteira começando pelo texto visível (WCAG 2.5.3)', () => {
  const t = textoDaFaixaNaoEntregues({ total: 6 })
  assert.ok(t.complemento, 'há complemento só para leitor de tela')
  assert.match(jsx, /className="sr"[^>]*>\{?[^<]*complemento/)
})

await caso('erro e carregando seguem iguais', () => {
  assert.equal(textoDaFaixaNaoEntregues({ total: 0, erro: true }).visivel, 'Mensagens sem atualizar')
  assert.equal(textoDaFaixaNaoEntregues({ total: 0, carregando: true }).visivel, 'Conferindo mensagens…')
})

await caso('o texto não quebra: uma linha com reticências, e "Revisar" não encolhe', () => {
  const texto = regra('rotuloNaoEntregues')
  assert.match(texto, /white-space:\s*nowrap/)
  assert.match(texto, /text-overflow:\s*ellipsis/)
  assert.match(texto, /overflow:\s*hidden/)
  assert.match(regra('verNaoEntregues'), /flex-shrink:\s*0/)
  assert.match(jsx, />Revisar</)
})

fim()
