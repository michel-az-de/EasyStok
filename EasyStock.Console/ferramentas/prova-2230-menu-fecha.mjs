/* eslint-disable no-console */
// Prova da issue #2230: os menus <details> do Atendimento (⋮ da conversa, "Mais ações" da faixa de
// quem responde e "Mais ferramentas" do campo) fecham com toque fora e com Escape, mesmo com o foco
// fora deles. A janela aberta de dentro do menu conta como dentro. Sem DOM no Node: documento e
// menu falsos, com o mesmo contrato que o navegador entrega.
//
//   node ferramentas/prova-2230-menu-fecha.mjs

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

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

// Nó falso: `contains` pela cadeia de pais, `closest('dialog')` pelo nome.
function no(nome, pai = null) {
  return {
    nome, pai, focado: false,
    contains(outro) { for (let n = outro; n; n = n.pai) if (n === this) return true; return false },
    closest(seletor) { for (let n = this; n; n = n.pai) if (seletor === 'dialog' && n.nome === 'dialog') return n; return null },
    focus() { documento.activeElement = this },
  }
}
const documento = {
  activeElement: null, ouvintes: {},
  addEventListener(tipo, fn) { (this.ouvintes[tipo] ??= new Set()).add(fn) },
  removeEventListener(tipo, fn) { this.ouvintes[tipo]?.delete(fn) },
  disparar(tipo, evento) { for (const fn of this.ouvintes[tipo] ?? []) fn(evento) },
}
function cena() {
  const corpo = no('body')
  const menu = no('details', corpo)
  menu.open = true
  const resumo = no('summary', menu)
  const item = no('button', menu)
  const janela = no('dialog', menu)
  janela.open = false
  const campoDaJanela = no('input', janela)
  menu.querySelector = (s) => (s === 'summary' ? resumo : s === 'dialog[open]' && janela.open ? janela : null)
  const fora = no('main', corpo)
  return { menu, resumo, item, janela, campoDaJanela, fora }
}
const tecla = (key, target, extra = {}) => ({
  key, target, defaultPrevented: false, preventDefault() { this.defaultPrevented = true }, ...extra,
})

let mod = {}
try { mod = await import('../src/hooks/menuDetalhes.js') } catch (erro) { console.log('      (sem src/hooks/menuDetalhes.js: ' + erro.code + ')') }
const ligar = (menu) => {
  assert.equal(typeof mod.ligarMenuDetalhes, 'function', 'falta ligarMenuDetalhes')
  return mod.ligarMenuDetalhes(documento, () => menu)
}

// --- 1. Toque -------------------------------------------------------------------
await confere('1. toque fora fecha o menu', () => {
  const c = cena(); const soltar = ligar(c.menu)
  documento.disparar('pointerdown', { target: c.fora })
  soltar()
  assert.equal(c.menu.open, false)
})
await confere('1. toque dentro (item, resumo ou janela aberta dele) não fecha', () => {
  const c = cena(); const soltar = ligar(c.menu)
  c.janela.open = true
  for (const alvo of [c.item, c.resumo, c.campoDaJanela]) documento.disparar('pointerdown', { target: alvo })
  soltar()
  assert.equal(c.menu.open, true)
})

// --- 2. Escape ------------------------------------------------------------------
await confere('2. Escape com o foco fora fecha o menu, sem roubar o foco', () => {
  const c = cena(); const soltar = ligar(c.menu)
  c.fora.focus()
  documento.disparar('keydown', tecla('Escape', c.fora))
  soltar()
  assert.equal(c.menu.open, false)
  assert.equal(documento.activeElement, c.fora)
})
await confere('2. Escape com o foco num item fecha e devolve o foco ao ⋮', () => {
  const c = cena(); const soltar = ligar(c.menu)
  c.item.focus()
  documento.disparar('keydown', tecla('Escape', c.item))
  soltar()
  assert.equal(c.menu.open, false)
  assert.equal(documento.activeElement, c.resumo)
})
await confere('2. Escape de uma janela aberta de dentro do menu fecha só a janela', () => {
  const c = cena(); const soltar = ligar(c.menu)
  c.janela.open = true
  documento.disparar('keydown', tecla('Escape', c.campoDaJanela))
  soltar()
  assert.equal(c.menu.open, true)
})
await confere('2. Escape já tratado por outra camada não fecha; outra tecla também não', () => {
  const c = cena(); const soltar = ligar(c.menu)
  documento.disparar('keydown', tecla('Escape', c.fora, { defaultPrevented: true }))
  documento.disparar('keydown', tecla('Enter', c.fora))
  soltar()
  assert.equal(c.menu.open, true)
})
await confere('2. menu fechado ou ausente: nada acontece; soltar desliga os ouvintes', () => {
  const c = cena(); c.menu.open = false
  const soltar = ligar(c.menu)
  documento.disparar('pointerdown', { target: c.fora })
  soltar()
  assert.equal(c.menu.open, false)
  const soltarVazio = mod.ligarMenuDetalhes(documento, () => null)
  documento.disparar('pointerdown', { target: c.fora })
  soltarVazio()
  assert.equal(documento.ouvintes.pointerdown.size + documento.ouvintes.keydown.size, 0)
})

// --- 3. Os três menus usam a mesma regra --------------------------------------------
const fonte = (caminho) => readFileSync(new URL('../src/' + caminho, import.meta.url), 'utf8')
for (const [arquivo, ref] of [
  ['features/atendimento/PainelAtendimento.jsx', 'menuConversa'],
  ['features/atendimento/BarraModo.jsx', 'menu'],
  ['features/atendimento/Composer.jsx', 'ferramentasRef'],
]) {
  await confere(`3. ${arquivo.split('/').pop()}: o <details> usa useMenuDetalhes(${ref})`, () => {
    const codigo = fonte(arquivo)
    assert.match(codigo, new RegExp(`useMenuDetalhes\\(${ref}\\)`))
    assert.match(codigo, new RegExp(`<details[^>]*ref=\\{${ref}\\}`))
  })
}
await confere('3. "Concluir conversa" do ⋮ fecha o menu ao abrir o resumo', () => {
  const codigo = fonte('features/atendimento/PainelAtendimento.jsx')
  assert.match(codigo, /fecharMenu\(menuConversa\.current\);\s*abrirEncerramento\(/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
