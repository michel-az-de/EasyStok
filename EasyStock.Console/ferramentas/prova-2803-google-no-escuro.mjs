/* eslint-disable no-console */
// Prova da issue #2803: o botão do Google não ganha fundo branco no tema escuro.
// O botão é um iframe de outra origem, sempre claro. A raiz do console declara `color-scheme: light dark`;
// no escuro o esquema do iframe difere do da página e o navegador pinta um fundo opaco atrás dele. O
// invólucro `.google` declara `color-scheme: light`, que o iframe herda, e os dois esquemas se igualam.
//
//   node ferramentas/prova-2803-google-no-escuro.mjs

import { readFileSync, readdirSync } from 'node:fs'
import assert from 'node:assert/strict'

const SRC = new URL('../src/', import.meta.url)
const fonte = (caminho) => readFileSync(new URL(caminho, SRC), 'utf8')
const semComentario = (css) => css.replace(/\/\*[\s\S]*?\*\//g, '')
const regra = (css, seletor) => {
  const escapado = seletor.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return [...semComentario(css).matchAll(new RegExp(`(?:^|\\n|\\})\\s*${escapado}\\s*\\{([^}]*)\\}`, 'g'))].map((m) => m[1]).join(' ')
}
const telas = readdirSync(SRC, { recursive: true })
  .map((caminho) => String(caminho).replaceAll('\\', '/'))
  .filter((caminho) => caminho.endsWith('.jsx') && fonte(caminho).includes('<BotaoGoogle'))

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log(`  ok  ${descricao}`) } catch (erro) { falhas.push(descricao); console.log(`  XX  ${descricao}\n      ${String(erro.message).split('\n').slice(0, 12).join('\n      ')}`) }
}

confere('o invólucro do botão do Google declara o esquema claro, que o iframe herda', () => {
  assert.match(regra(fonte('features/login/login.module.css'), '.google'), /(?:^|;)\s*color-scheme:\s*light\s*;/, '.google precisa de `color-scheme: light`')
})

confere('todo lugar que desenha o BotaoGoogle usa esse invólucro', () => {
  assert.ok(telas.length > 0, 'nenhuma tela desenha o BotaoGoogle: a prova perdeu o alvo')
  for (const caminho of telas) {
    const jsx = fonte(caminho)
    assert.match(jsx, /import css from '\.\/login\.module\.css'/, `${caminho}: o css vem de login.module.css`)
    const desenhos = jsx.match(/<BotaoGoogle\b/g).length
    const noInvolucro = (jsx.match(/<div className=\{css\.google\}>\s*<BotaoGoogle\b/g) ?? []).length
    assert.equal(noInvolucro, desenhos, `${caminho}: BotaoGoogle fora de <div className={css.google}>`)
  }
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
