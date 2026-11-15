// Preload das provas (#2701): no Node puro `import.meta.env` não existe, e quem importa `src/` caía em
// `src/infra/fonteDados.js` com "reading 'VITE_FONTE_DADOS'". O Vite injeta esse objeto em todo módulo; aqui o
// load hook faz o mesmo nos arquivos de `src/` que o leem. O ambiente é fixo, o do console sem `.env` (demonstração),
// e não lê `process.env`: o rodar-provas grava VITE_FONTE_DADOS=api quando sobe o Vite das provas de navegador, e a
// prova de Node não pode mudar de modo conforme a máquina tem ou não Chromium. Prova que precisa do modo API troca
// `fonteDados.js` no próprio load hook, que roda antes deste.
//
//   node --import ./ferramentas/ambienteDoVite.mjs ferramentas/prova-x.mjs   (o rodar-provas já passa)
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'

export const AMBIENTE_DO_VITE = Object.freeze({ BASE_URL: '/', MODE: 'production', DEV: false, PROD: true, SSR: false })

const DE_SRC = /\/EasyStock\.Console\/src\/.+\.m?jsx?$/
const DO_CONSOLE = /^file:.*\/EasyStock\.Console\/(?!node_modules\/)/

registerHooks({
  load(url, contexto, proximo) {
    // O Vite também aceita `import x from './a.json'` sem `with { type: 'json' }` (a massa de demonstração);
    // o Node exige o atributo. Mesmo tratamento que várias provas já faziam no próprio hook. Só o JSON do
    // console: o `require()` de package.json dentro do node_modules (rolldown, vite) passa pelo mesmo hook.
    if (DO_CONSOLE.test(url) && url.endsWith('.json') && !contexto.importAttributes?.type) {
      return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    }
    const carregado = proximo(url, contexto)
    if (!url.startsWith('file:') || !DE_SRC.test(url) || carregado.source == null) return carregado
    const fonte = String(carregado.source)
    if (!fonte.includes('import.meta.env')) return carregado
    // Na mesma linha do início, para não deslocar a numeração das pilhas de erro.
    return { ...carregado, source: `import.meta.env ??= ${JSON.stringify(AMBIENTE_DO_VITE)};${fonte}` }
  },
})
