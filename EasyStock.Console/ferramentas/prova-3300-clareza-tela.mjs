/* eslint-disable no-console */
// Prova da #3300 (F2, F4, F5, F6) por leitura do código. A medida no navegador (390 e 1440) está na PR.
import { assert, caso, fim, fonte } from './prova-2944-base.mjs'

const thread = fonte('features/atendimento/Thread.jsx')
const css = fonte('features/atendimento/atendimento.module.css')
const composer = fonte('features/atendimento/Composer.jsx')
const balao = fonte('features/assistente/BalaoAssistente.jsx')

await caso('F2: a divisória tem a parte longa separada, que some no celular', () => {
  assert.match(thread, /className=\{css\.divisoriaResto\}/)
  assert.match(css, /@media \(max-width: 759px\)[^}]*\{\s*\.divisoriaResto \{ display: none; \}/)
})

await caso('F2: a divisória fica numa linha só, com reticência', () => {
  assert.match(css, /\.divisoriaAtendimento > span \{[^}]*white-space: nowrap;[^}]*text-overflow: ellipsis;/)
})

await caso('F4: com texto ou anexo (#3368), "Assumir conversa" cede o laranja a "Enviar e assumir"', () => {
  assert.match(css, /:has\(\.composer \.enviar:not\(\.gravar\):not\(:disabled\)\) \.modoAssumir/)
})

await caso('F5: a dica do Ctrl+Enter não aparece em tela de toque', () => {
  assert.match(balao, /pointer: coarse/)
  assert.doesNotMatch(balao, /dica="Ctrl\+Enter para perguntar\. Enter cria uma nova linha\."/)
})

await caso('F6: o rótulo visível da nota tem classe que cede no celular e o aria-label segue completo', () => {
  assert.match(composer, /aria-label=\{notaEmRascunho \? 'Retomar nota interna' : 'Nota interna'\}/)
  assert.match(composer, /className=\{css\.rotuloNota\}/)
  assert.match(css, /\.rotuloNota \{ display: none; \}/)
})

fim()
