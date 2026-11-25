/* eslint-disable no-console */
// Prova da #3322 (WCAG 2.5.7): a largura do Assistente e do Cardápio muda sem arrastar. Um botão "Largura" na barra
// de cada painel abre um controle deslizante, como o da Disposição, que chama a mesma função da alça.
import { readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import { transformWithOxc } from 'vite'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { assert, caso, fim, fonte } from './prova-2944-base.mjs'

const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const jsx = new Map()
for (const n of readdirSync(join(raiz, 'componentes')).filter(n => n.endsWith('.jsx'))) {
  const arquivo = join(raiz, 'componentes', n)
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env.DEV', 'false').replaceAll('import.meta.env?.DEV', 'false')
  jsx.set(pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code)
}
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
    return proximo(url, contexto)
  },
})

const { AjusteLargura, PainelLargura } = await import('../src/componentes/AjusteLargura.jsx')
const html = (componente, props) => renderToStaticMarkup(createElement(componente, props))

await caso('o botão fechado diz o que ajusta e que abre um diálogo', () => {
  const h = html(AjusteLargura, { nome: 'do assistente', valor: 400, min: 320, max: 640, padrao: 420, aoMudar() {} })
  assert.match(h, /aria-label="Largura do assistente"/)
  assert.match(h, /aria-expanded="false"/)
  assert.match(h, /aria-haspopup="dialog"/)
})

await caso('o painel tem controle deslizante com os limites da alça e o valor atual', () => {
  const h = html(PainelLargura, { nome: 'do cardápio', valor: 460, min: 380, max: 720, padrao: 460, aoMudar() {} })
  assert.match(h, /type="range"/)
  assert.match(h, /aria-label="Largura do cardápio"/)
  assert.match(h, /min="380"/)
  assert.match(h, /max="720"/)
  assert.match(h, /value="460"/)
  assert.match(h, /460 px/)
  assert.match(h, />Restaurar largura</)
})

await caso('Assistente e Cardápio usam o botão com a mesma função da alça', () => {
  const assistente = fonte('features/assistente/BalaoAssistente.jsx')
  const cardapio = fonte('features/cardapio/PainelCardapio.jsx')
  assert.match(assistente, /<AjusteLargura[\s\S]*?nome="do assistente"[\s\S]*?aoMudar=\{mudarLargura\}/)
  assert.match(assistente, /<AlcaLargura[\s\S]*?aoMudar=\{mudarLargura\}/)
  assert.match(cardapio, /<AjusteLargura[\s\S]*?nome="do cardápio"[\s\S]*?aoMudar=\{aoRedimensionar\}/)
  assert.match(cardapio, /<AlcaLargura[\s\S]*?aoMudar=\{aoRedimensionar\}/)
})

fim()
