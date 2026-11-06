// Prova da #2258: padrões de sistema da auditoria de UX/UI (Casa da Babá, 05/11) nos componentes.
//
// Cobre as regras que valem para todas as telas: "Descartar o que foi digitado?" ao fechar um
// formulário com dados (F4, T17), botão desabilitado com cara de desabilitado e com o motivo
// escrito (P6, T14), erro sob o campo ligado por aria-describedby e com contorno visível (T12),
// texto à esquerda e dinheiro à direita (T15). O desenho em si é conferido no navegador.
//
// Roda com: node ferramentas/prova-2258-componentes.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> node ferramentas/prova-2258-componentes.mjs

import { readFileSync } from 'node:fs'
import { pathToFileURL } from 'node:url'
import assert from 'node:assert/strict'

const base = process.env.PROVA_SRC ? pathToFileURL(process.env.PROVA_SRC.replace(/[\\/]?$/, '/')) : new URL('../src/', import.meta.url)
const ler = (caminho) => readFileSync(new URL(caminho, base), 'utf8')
const regra = (texto, seletor) => {
  const inicio = texto.indexOf(`${seletor} {`)
  assert.notEqual(inicio, -1, `regra ${seletor} não encontrada`)
  return texto.slice(inicio, texto.indexOf('}', inicio))
}
let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok    ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA ${nome}\n      ${erro.message.split('\n')[0]}`) }
}

caso('F4/T17: o Modal pergunta antes de descartar o que foi digitado', () => {
  const modal = ler('componentes/Modal.jsx')
  assert.match(modal, /confirmarDescarte = false/)
  assert.match(modal, /Descartar o que foi digitado\?/)
  assert.match(modal, /Continuar editando/)
  assert.match(modal, /variante="perigo"[\s\S]{0,120}>Descartar<\/Botao>/)
  // Esc (evento cancel/close do <dialog>) e o × passam pela mesma pergunta.
  assert.match(modal, /if \(descarteRef\.current\) \{? ?abrirPergunta\(\)/)
  // banca a11y: "Continuar editando" e Esc devolvem o foco ao campo que estava em edição.
  assert.match(modal, /onClick=\{voltarAEdicao\}>Continuar editando/)
  assert.match(modal, /campo\?\.focus\(/)
})

caso('F4/T17: fechar caixa e lançar entrada/saída usam a pergunta', () => {
  assert.match(ler('features/gestao/caixa/ModalFecharCaixa.jsx'), /confirmarDescarte=\{centavosContado != null\}/)
  assert.match(ler('features/gestao/caixa/AbaCaixaApi.jsx'), /confirmarDescarte=\{digitado\}/)
})

caso('P6/T14: desabilitado não tem o mesmo fundo do botão ativo', () => {
  const css = ler('componentes/Botao.module.css')
  const desabilitado = regra(css, '.botao:disabled')
  assert.doesNotMatch(desabilitado, /var\(--preenchimento\)/)
  assert.match(desabilitado, /dashed/)
  assert.match(desabilitado, /cursor: not-allowed/)
})

caso('P6/T14: o motivo do desabilitado fica escrito e é lido pelo leitor de tela', () => {
  const botao = ler('componentes/Botao.jsx')
  assert.match(botao, /motivo/)
  assert.match(botao, /aria-describedby/)
  assert.match(botao, /id=\{idMotivo\}/)
})

caso('T12: erro sob o campo, ligado por aria-describedby, com aria-invalid e contorno', () => {
  const campo = ler('componentes/Campo.jsx')
  assert.match(campo, /\$\{id\}-erro/)
  assert.match(campo, /aria-invalid=\{erro \? true : undefined\}/)
  assert.match(campo, /role="alert"/)
  assert.match(regra(ler('componentes/Campo.module.css'), ".controle[aria-invalid='true']"), /var\(--erro\)/)
})

caso('T12/T13: o nome em edição não some no erro, recebe o foco e tem dica e limite', () => {
  const emLinha = ler('features/ficha-cliente/CampoEmLinha.jsx')
  assert.match(emLinha, /aria-describedby=\{erro \? idErro : undefined\}/)
  assert.match(emLinha, /if \(erro && editando\) inputRef\.current\?\.focus\(\)/)
  assert.match(emLinha, /placeholder=\{dica\}/)
  assert.match(emLinha, /maxLength=\{limite\}/)
  const comErro = regra(ler('features/ficha-cliente/cliente.module.css'), '.controleComErro')
  assert.match(comErro, /box-shadow: inset 0 0 0 2px var\(--erro\)/)
})

caso('T15: telefone e CEP à esquerda, só dinheiro à direita', () => {
  const css = ler('componentes/CampoMascarado.module.css')
  assert.match(regra(css, '.controle'), /text-align: left/)
  assert.match(regra(css, ".controle[data-tipo='moeda']"), /text-align: right/)
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
