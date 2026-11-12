/* eslint-disable no-console */
// Prova da issue #2230: os menus do Atendimento (⋮ da conversa e "Mais ferramentas" do campo) fecham
// com toque fora e com Escape, mesmo com o foco fora deles, e o item fecha o menu antes de agir.
//
// #2185 (menu único): os dois <details> com useMenuDetalhes viraram MenuDeAcoes, e o fechamento é do
// Popover. Esta prova confere que o Atendimento usa o menu único e que o Popover guarda o contrato da
// #2230; o comportamento no navegador está na prova-2185-menu-de-acoes (casos 3: escolher, Esc e fora).
//
//   node ferramentas/prova-2230-menu-fecha.mjs

import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}
const url = (caminho) => new URL('../src/' + caminho, import.meta.url)
const fonte = (caminho) => readFileSync(url(caminho), 'utf8')

// --- 1. O Atendimento usa o menu único -------------------------------------------------
// #2232 (A3, A4): o "Mais ações" da faixa saiu; Devolver e Concluir moram no ⋮ do cabeçalho.
await confere('1. BarraModo.jsx: a faixa não tem menu próprio (a conversa tem um menu só)', () => {
  assert.doesNotMatch(fonte('features/atendimento/BarraModo.jsx'), /<details|<MenuDeAcoes/)
})
for (const arquivo of ['features/atendimento/PainelAtendimento.jsx', 'features/atendimento/Composer.jsx']) {
  await confere(`1. ${arquivo.split('/').pop()}: o menu é o MenuDeAcoes, sem <details>`, () => {
    const codigo = fonte(arquivo)
    assert.doesNotMatch(codigo, /<details/)
    assert.match(codigo, /<MenuDeAcoes\b/)
  })
}
await confere('1. o fechamento do <details> saiu junto com o último <details> de menu', () => {
  assert.equal(existsSync(url('hooks/useMenuDetalhes.js')), false)
  assert.equal(existsSync(url('hooks/menuDetalhes.js')), false)
})

// --- 2. O item fecha o menu antes de agir ------------------------------------------------
await confere('2. MenuDeAcoes: escolher fecha o menu e só então faz a ação', () => {
  assert.match(fonte('componentes/MenuDeAcoes.jsx'), /onClick=\{\(\) => \{ fechar\(\); item\.aoEscolher\(\) \}\}/)
})
await confere('2. "Concluir conversa" do ⋮ abre o resumo pelo item do menu', () => {
  assert.match(fonte('features/atendimento/PainelAtendimento.jsx'),
    /chave: 'concluir', rotulo: 'Concluir conversa'[^\n]*aoEscolher: \(\) => abrirEncerramento\(/)
})

// --- 3. O Popover guarda o contrato da #2230 ---------------------------------------------
const popover = fonte('componentes/Popover.jsx')
await confere('3. toque fora fecha: pointerdown no documento, fora da caixa', () => {
  assert.match(popover, /document\.addEventListener\('pointerdown', aoClicarFora\)/)
  assert.match(popover, /if \(!caixaRef\.current\?\.contains\(evento\.target\)\) aoFechar\(\)/)
})
await confere('3. Escape no documento fecha com o foco em qualquer lugar e devolve o foco ao gatilho', () => {
  assert.match(popover, /useEscape\(true, \(evento\) => \{[\s\S]*?aoFechar\(\)\s*\n\s*if \(origemRef\.current\?\.isConnected\) origemRef\.current\.focus\(\)/)
  assert.match(popover, /\{ capturar: true \}\)/)
  assert.match(fonte('hooks/useEscape.js'), /document\.addEventListener\('keydown', aoTeclar, capturar\)/)
})
await confere('3. Escape de uma janela aberta sobre o menu é dela; Escape já tratado não fecha', () => {
  assert.match(popover, /if \(janelaDoMenu !== janelaDoFoco\) return/)
  assert.match(fonte('hooks/useEscape.js'), /evento\.key === 'Escape' && !evento\.defaultPrevented/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
