// Prova da #2792 (frente E): Gestão > Cadastros tem uma rolagem só e as abas de pessoas são abas de verdade.
// - `.lista` não tem overflow nem max-height (era uma segunda rolagem de 62vh dentro da página).
// - As abas Todos/Leads/Clientes/Sem cadastro usam BarraDeAbas com `rotulo`, sem role=group nem aria-pressed.
// - O CSS morto `.abas` saiu.
//
// Roda com: node ferramentas/prova-2792-cadastros-lista.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> node ferramentas/prova-2792-cadastros-lista.mjs

import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import assert from 'node:assert/strict'

const raiz = process.env.PROVA_SRC ? process.env.PROVA_SRC.replace(/[\/]$/, '') : fileURLToPath(new URL('../src', import.meta.url))
const ler = (rel) => readFileSync(join(raiz, 'features/gestao/cadastros', rel), 'utf8')
const css = ler('cadastros.module.css')
const painel = ler('PainelCadastros.jsx')
const semComentarioCss = css.replace(/\/\*[\s\S]*?\*\//g, '')
const regrasDe = (seletor) => [...semComentarioCss.matchAll(/([^{}]+)\{([^}]*)\}/g)].filter((m) => m[1].split(',').some((s) => s.trim() === seletor)).map((m) => m[2])

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok    ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA ${nome}\n      ${erro.message.split('\n')[0]}`) }
}

caso('.lista não tem overflow nem max-height (uma rolagem só, a da página)', () => {
  const regras = regrasDe('.lista')
  assert.ok(regras.length > 0, '.lista sumiu do CSS')
  for (const corpo of regras) assert.doesNotMatch(corpo, /overflow\s*:|max-height\s*:/, `.lista ainda rola sozinha: ${corpo.trim()}`)
})

caso('o cabeçalho da tabela gruda no topo da página', () => {
  assert.ok(regrasDe('.lista th').some((corpo) => /position\s*:\s*sticky/.test(corpo)), '.lista th sem position: sticky')
})

caso('as abas de pessoas usam BarraDeAbas com rotulo', () => {
  assert.match(painel, /<BarraDeAbas[^>]*rotulo="Tipos de cadastro"/, 'PainelCadastros sem BarraDeAbas rotulo="Tipos de cadastro"')
  assert.match(painel, /role=\{tela === 'clientes' \? 'tabpanel'/, 'falta o tabpanel das abas')
})

caso('as abas de pessoas não usam role=group nem aria-pressed', () => {
  assert.doesNotMatch(painel, /aria-pressed/, 'PainelCadastros ainda usa aria-pressed')
  assert.doesNotMatch(painel, /role="group"/, 'PainelCadastros ainda usa role="group"')
})

caso('o CSS morto .abas saiu', () => {
  assert.doesNotMatch(semComentarioCss, /\.abas\b/, 'cadastros.module.css ainda tem .abas')
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
