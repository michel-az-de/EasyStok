// Regras locais do oxlint (#3401), carregadas por `jsPlugins` no .oxlintrc.json.
//
// local/import-existe: o oxlint não tem `import/no-unresolved`. Sem esta regra, `import x from './naoExiste'`
// (erro típico de código escrito por IA) passava no lint e só caía no build. Barra:
//   - especificador relativo sem arquivo, com as extensões que o Vite resolve no src, e com a caixa exata do
//     último nome (o Windows acha `./botao` para Botao.jsx; o build do CI em Linux, não);
//   - pacote que não está no package.json mais próximo (pacote inventado ou dependência fantasma). Confere o
//     package.json, não o node_modules: worktree sem npm ci não dá falso positivo.
// Não olha caminho absoluto (`/logo.svg`, servido de public/), `node:`, `virtual:`, URL nem import com template.
// JS plugins do oxlint são alpha: a prova-3401-lint-na-edicao.mjs quebra se a API mudar.
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs'
import { builtinModules } from 'node:module'
import { basename, dirname, join, resolve } from 'node:path'

const EXTENSOES = ['', '.js', '.jsx', '.mjs', '.json', '/index.js', '/index.jsx']
const NATIVOS = new Set(builtinModules)
const pacotesPorPasta = new Map()

const ehArquivo = (caminho) => existsSync(caminho) && statSync(caminho).isFile()
const nomeExato = (caminho) => readdirSync(dirname(caminho)).includes(basename(caminho))
const nomeDoPacote = (especificador) => especificador.split('/').slice(0, especificador.startsWith('@') ? 2 : 1).join('/')

// A caixa se confere no último nome escrito no import: o arquivo, ou a pasta quando resolve para index.
function arquivoDoImport(base) {
  const extensao = EXTENSOES.find((candidata) => ehArquivo(base + candidata))
  if (extensao === undefined) return 'não existe'
  return nomeExato(extensao.startsWith('/') ? base : base + extensao) ? null : 'não existe com essa caixa no nome'
}

function pacotesDeclarados(pasta) {
  if (pacotesPorPasta.has(pasta)) return pacotesPorPasta.get(pasta)
  const manifesto = join(pasta, 'package.json')
  let pacotes = null
  if (existsSync(manifesto)) {
    const p = JSON.parse(readFileSync(manifesto, 'utf8'))
    pacotes = new Set([p.name, ...Object.keys({ ...p.dependencies, ...p.devDependencies, ...p.peerDependencies, ...p.optionalDependencies })])
  } else if (dirname(pasta) !== pasta) {
    pacotes = pacotesDeclarados(dirname(pasta))
  }
  pacotesPorPasta.set(pasta, pacotes)
  return pacotes
}

function problema(especificador, arquivo) {
  const limpo = especificador.split(/[?#]/)[0]
  if (limpo.startsWith('.')) {
    const falta = arquivoDoImport(resolve(dirname(arquivo), limpo))
    return falta && `arquivo "${especificador}" ${falta}`
  }
  if (!limpo || limpo.startsWith('/') || /^[a-z][a-z0-9+.-]*:/i.test(limpo) || NATIVOS.has(limpo)) return null
  const pacotes = pacotesDeclarados(dirname(resolve(arquivo)))
  const nome = nomeDoPacote(limpo)
  return !pacotes || pacotes.has(nome) ? null : `pacote "${nome}" não está no package.json`
}

export default {
  meta: { name: 'local' },
  rules: {
    'import-existe': {
      meta: { type: 'problem', docs: { description: 'import de arquivo ou pacote que não existe' } },
      create(context) {
        const checar = (fonte) => {
          if (typeof fonte?.value !== 'string') return
          const mensagem = problema(fonte.value, context.filename)
          if (mensagem) context.report({ node: fonte, message: mensagem })
        }
        return {
          ImportDeclaration: (no) => checar(no.source),
          ExportNamedDeclaration: (no) => checar(no.source),
          ExportAllDeclaration: (no) => checar(no.source),
          ImportExpression: (no) => checar(no.source),
        }
      },
    },
  },
}
