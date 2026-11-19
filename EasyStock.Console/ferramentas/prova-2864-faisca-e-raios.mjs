/* eslint-disable no-console */
// Prova da issue #2864: o Assistente sem a faísca de "feito por IA" e o raio dito pelo papel da peça.
//
//   node ferramentas/prova-2864-faisca-e-raios.mjs

import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import { join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const fonte = (caminho) => readFileSync(join(raiz, caminho), 'utf8')
const token = (css, nome) => (css.match(new RegExp(`--${nome}:\\s*([^;]+);`)) || [])[1]?.trim()

// Arquivos do fluxo do Assistente que a PR #2858 (#2857) ainda está mexendo: migram depois, pelo apelido.
const DO_FLUXO = ['dominio/desenhoDoFluxo.js', 'features/gestao/atendimento/PainelAssistente.jsx']

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log(`  ok  ${descricao}`) } catch (erro) { falhas.push(descricao); console.log(`  XX  ${descricao}\n      ${String(erro.message).split('\n')[0]}`) }
}

confere('raio por papel: controle 6 px, cartão 10 px, painel 14 px', () => {
  const tokens = fonte('estilos/tokens.css')
  assert.equal(token(tokens, 'r-1'), '6px')
  assert.equal(token(tokens, 'r-2'), '10px')
  assert.equal(token(tokens, 'r-3'), '14px')
})

confere('o conceito agente desenha o robô, e sparkles é só apelido dele', () => {
  const icone = fonte('componentes/Icone.jsx')
  assert.match(icone, /\bagente: Bot\b/)
  assert.match(icone, /\bsparkles: Bot\b/)
  assert.doesNotMatch(icone, /\bSparkles\b/, 'o desenho da faísca sai do pacote')
})

confere('telas pedem o ícone pelo conceito, não pela faísca', () => {
  const usos = []
  for (const nome of readdirSync(raiz, { recursive: true })) {
    if (!/\.jsx?$/.test(nome)) continue
    const rel = relative(raiz, join(raiz, nome)).split(sep).join('/')
    if (DO_FLUXO.includes(rel) || rel === 'componentes/Icone.jsx') continue
    if (/(?:nome|icone)(?:=|:\s*)\{?\s*['"]sparkles['"]/.test(fonte(rel))) usos.push(rel)
  }
  assert.deepEqual(usos, [])
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
