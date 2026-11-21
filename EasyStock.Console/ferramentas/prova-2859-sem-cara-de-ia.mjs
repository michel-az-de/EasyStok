/* eslint-disable no-console */
// Prova da issue #2859: o Console sem os tiques de interface gerada que a revisão do Impeccable apontou.
//
//   node ferramentas/prova-2859-sem-cara-de-ia.mjs

import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { transformWithOxc } from 'vite'

// Carrega .jsx e .css pelo mesmo gancho da prova 1839.
const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter((n) => n.endsWith('.jsx')).map(async (nome) => {
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
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return proximo(url, contexto)
  },
})

const { createElement: h } = await import('react')
const { renderToStaticMarkup } = await import('react-dom/server')

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const { PainelAgora } = await import('../src/features/agora/PainelAgora.jsx')

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log(`  ok  ${descricao}`) } catch (erro) { falhas.push(descricao); console.log(`  XX  ${descricao}\n      ${String(erro.message).split('\n')[0]}`) }
}

const item = (id) => ({ id, nome: `Cliente ${id}`, rotulo: 'Esperando você', texto: 'Falou por último', numero: id, situacao: 'Em preparo' })
const painel = renderToStaticMarkup(h(PainelAgora, {
  filas: { responder: [item('a'), item('b')], cozinhar: [item('c')], despachar: [] },
  podeVer: { atendimento: true, cozinha: true, entregas: true },
  aoAbrirConversa: () => {},
}))

confere('easing de pulo e mola sem passar do ponto', () => {
  const tokens = fonte('estilos/tokens.css')
  assert.doesNotMatch(tokens, /cubic-bezier\([^)]*1\.\d+[^)]*\)/, 'nenhum cubic-bezier com overshoot')
  assert.doesNotMatch(tokens, /--ease-mola:\s*linear\(/, 'mola sem curva elástica')
})

confere('painel Agora sem rótulo solto acima das filas', () => {
  assert.doesNotMatch(painel, /<p[^>]*>Agora<\/p>/)
})

confere('contagem da fila fica no título, sem número de vitrine ao lado', () => {
  assert.match(painel, /<h2[^>]*>Responder(?:<!-- -->)? ?<span[^>]*>2<\/span><\/h2>/, 'Responder leva a contagem no h2')
  assert.match(painel, /<h2[^>]*>Despachar(?:<!-- -->)? ?<span[^>]*>0<\/span><\/h2>/, 'fila vazia mostra 0 no h2')
  assert.doesNotMatch(fonte('features/agora/agora.module.css'), /clamp\(2rem/, 'sem número em 2 a 2,75 rem')
})

confere('Hall sem a faixa que repetia o cartão do módulo', () => {
  const hall = fonte('features/hall/HallDeModulos.jsx')
  assert.doesNotMatch(hall, /destaque-hall|acaoDestaque/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
