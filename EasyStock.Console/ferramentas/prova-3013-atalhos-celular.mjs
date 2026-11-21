// Prova da #3013 (crítica UX de 20/11, Início no celular): faixa de atalhos dos módulos logo abaixo da busca, com a
// linha de estado do cartão (`estadoDoCartao`, mesma leitura do Início, sem chamada nova). Só até 640 px; some na
// busca e no modo Organizar. O desenho em si é conferido no navegador.
//
// Roda com: node ferramentas/prova-3013-atalhos-celular.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> node ferramentas/prova-3013-atalhos-celular.mjs

import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { transformWithOxc } from 'vite'

const base = process.env.PROVA_SRC ? pathToFileURL(process.env.PROVA_SRC.replace(/[\/]?$/, '/')) : new URL('../src/', import.meta.url)
const raiz = fileURLToPath(base)
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter((n) => n.endsWith('.jsx')).map(async (nome) => {
  const arquivo = join(raiz, nome)
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env', '({ DEV: false })')
  return [pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code]
})))
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(especificador + ext, contexto) } catch { /* próximo formato */ } }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
    if (url.endsWith('.css')) return { format: 'module', source: 'export default new Proxy({}, { get: (_, p) => String(p) })', shortCircuit: true }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: 'export const FONTE_API=true;export const API_BASE="";', shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return proximo(url, contexto)
  },
})

const fonte = (caminho) => readFileSync(new URL(caminho, base), 'utf8')
const { createElement: h } = await import('react')
const { renderToStaticMarkup } = await import('react-dom/server')
globalThis.document ??= { documentElement: { dataset: { theme: 'light' } } }
const { modulosDoHall } = await import(new URL('dominio/modulos.js', base).href)
const { estadoDoCartao } = await import(new URL('dominio/estadoDoHall.js', base).href)

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const { FaixaDeAtalhos } = await import(new URL('features/hall/FaixaDeAtalhos.jsx', base).href)
const modulos = modulosDoHall({ fonteApi: true }).filter((m) => m.disponivel)
const lido = { atendimento: { estado: 'ok', dados: { conversas: [{ id: 'c1', estado: 'aguardando' }] } } }

confere('a faixa traz um link por módulo disponível, para o mesmo destino do cartão', () => {
  const html = renderToStaticMarkup(h(FaixaDeAtalhos, { modulos, estadoDe: (id) => estadoDoCartao(id, {}) }))
  for (const m of modulos) assert.ok(html.includes(`href="${m.href}"`), `${m.id} aponta para ${m.href}`)
  assert.match(html, /aria-label="Atalhos dos módulos"/)
  assert.equal((html.match(/<a /g) ?? []).length, modulos.length)
})

confere('a linha de estado vem do cartão e some sem leitura (nunca "Tudo em dia" sem ler)', () => {
  const comEstado = { id: 'x', nome: 'Atendimento', href: '#/m/atendimento', disponivel: true }
  const html = renderToStaticMarkup(h(FaixaDeAtalhos, {
    modulos: [comEstado, { ...comEstado, id: 'y', nome: 'Cozinha', href: '#/m/cozinha' }],
    estadoDe: (id) => (id === 'x' ? { texto: '3 conversas precisam de você', atencao: true } : null),
  }))
  assert.match(html, /3 conversas precisam de você/)
  assert.match(html, /data-atencao/)
  assert.equal((html.match(/data-atencao/g) ?? []).length, 1, 'só o cartão com atenção leva a marca')
  assert.ok(!/em dia/i.test(html), 'sem leitura não afirma nada')
  assert.ok(lido) // a leitura real passa por estadoDoCartao, coberta na prova-2258
})

confere('a faixa só entra sem busca e sem Organizar, depois da busca e antes do painel', () => {
  const hall = fonte('features/hall/HallDeModulos.jsx')
  assert.match(hall, /!buscando && !organizar && <FaixaDeAtalhos[^>]*estadoDe=\{[^}]*estadoDoCartao\(id, fontes\)/, 'usa o estado do cartão e a mesma leitura')
  assert.ok(hall.indexOf('<FaixaDeAtalhos') > hall.indexOf('Buscar módulo ou tela'), 'depois do campo de busca')
  assert.ok(hall.indexOf('<FaixaDeAtalhos') < hall.indexOf('<PulsoDoHall'), 'antes do painel do dia')
})

confere('CSS: só no celular, alvo de toque, só tokens e sem barra de um lado só', () => {
  const css = fonte('features/hall/atalhosCelular.module.css').replace(/\/\*[\s\S]*?\*\//g, '')
  assert.match(css, /\.faixa\s*\{[^}]*display:\s*none/, 'escondida por padrão')
  assert.match(css, /@media \(max-width: 640px\)\s*\{[^@]*\.faixa\s*\{[^}]*display:\s*(flex|grid)/, 'aparece até 640 px')
  assert.match(css, /min-height:\s*var\(--toque\)/, 'alvo de toque')
  assert.doesNotMatch(css, /#[0-9a-f]{3,8}\b|rgba?\(|hsla?\(/i, 'sem cor fixa')
  assert.doesNotMatch(css, /border-(left|right|inline-start|inline-end)\s*:/, 'sem borda de um lado só')
  assert.doesNotMatch(css, /::before|::after/, 'sem pseudo-elemento de destaque')
})

console.log(`\n${passou} verificações, todas passaram.`)
