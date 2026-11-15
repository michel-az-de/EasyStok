/* eslint-disable no-console */
// Prova da #2656 (homologação #1769, Cardápio C1 e C2): na lista de Categorias,
//   - Excluir parado em categoria com pratos diz por que, com o `motivo` do Botao (#2258): texto visível
//     ligado por aria-describedby, não só `title`;
//   - "Ocultar no site" / "Mostrar no site" nomeia a categoria, como Renomear e Excluir da mesma linha.
// Componente real, renderizado no Node (mesmo gancho de `prova-2227-lista-carregando.mjs`):
//
//   node ferramentas/prova-2656-categorias-motivo.mjs

import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import { transformWithOxc } from 'vite'
import { createElement as h } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'

const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter(n => n.endsWith('.jsx')).map(async nome => {
  const arquivo = join(raiz, nome)
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env', '({ DEV: false })')
  return [pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code]
})))
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(spec + ext, contexto) } catch { /* próximo formato */ } }
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
    if (url.endsWith('.css')) return { format: 'module', source: 'export default new Proxy({}, { get: (_, p) => String(p) })', shortCircuit: true }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: 'export const FONTE_API=true;export const API_BASE="";', shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return proximo(url, contexto)
  },
})

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

const { LinhaCategoria } = await import('../src/features/gestao/cardapio/AbaCategoriasApi.jsx')
const gestao = { renomear() {}, alternarVisivel() {}, excluir() {}, mover() {}, fecharAviso() {} }
const linha = (secao) => renderToStaticMarkup(h('ol', null, h(LinhaCategoria, { secao, primeira: true, ultima: true, gestao })))
const botao = (html, texto) => html.match(new RegExp(`<button[^>]*>(?:(?!</button>).)*${texto}(?:(?!</button>).)*</button>`, 's'))?.[0] ?? ''

await confere('C1: Excluir parado em categoria com pratos diz o motivo em texto ligado ao botão', () => {
  const html = linha({ id: 's1', nome: 'Massas', itens: 3, visivel: true })
  const excluir = botao(html, 'Excluir')
  assert.match(excluir, /disabled/)
  const id = excluir.match(/aria-describedby="([^"]+)"/)?.[1]
  assert.ok(id, 'Excluir sem aria-describedby')
  assert.match(html, new RegExp(`<span id="${id}"[^>]*>[^<]*prato`), 'o motivo não está visível na linha')
})

await confere('C1: categoria vazia exclui sem motivo', () => {
  const excluir = botao(linha({ id: 's2', nome: 'Doces', itens: 0, visivel: true }), 'Excluir')
  assert.doesNotMatch(excluir, /disabled/)
  assert.doesNotMatch(excluir, /aria-describedby/)
})

await confere('C2: Ocultar no site nomeia a categoria', () => {
  assert.match(botao(linha({ id: 's1', nome: 'Massas', itens: 3, visivel: true }), 'Ocultar no site'), /aria-label="Ocultar Massas no site"/)
})

await confere('C2: Mostrar no site nomeia a categoria', () => {
  assert.match(botao(linha({ id: 's1', nome: 'Massas', itens: 3, visivel: false }), 'Mostrar no site'), /aria-label="Mostrar Massas no site"/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
