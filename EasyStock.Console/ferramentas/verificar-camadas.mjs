// Verifica a fronteira entre camadas. A dependência só desce, nunca sobe, e
// feature nenhuma importa outra feature.
//
// Ordem, de baixo para cima:
//   dominio  ->  (nada: funcoes puras, o dado chega por parametro)
//   infra    ->  dominio
//   hooks    ->  (nada do projeto)
//   componentes -> hooks
//   aplicacao   -> dominio, infra
//   features    -> dominio, componentes, hooks, aplicacao (catalogo vem por contexto)
//   app         -> tudo
//   demonstracao -> tudo, menos app (laboratorio local, issue #2017). Ninguem importa daqui por
//                  `import ... from`: so as duas portas (`infra/demonstracao.js` e `app/demonstracao.js`)
//                  carregam o pacote, por `import()` atras de um ramo que o build de producao elimina.
//
// Roda com: node ferramentas/verificar-camadas.mjs

import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, resolve, dirname } from 'node:path'

const RAIZ = resolve('src')

const PERMITIDO = {
  dominio: ['dominio'],
  infra: ['infra', 'dominio'],
  hooks: ['hooks'],
  componentes: ['componentes', 'hooks'],
  aplicacao: ['aplicacao', 'dominio', 'infra'],
  features: ['features', 'dominio', 'componentes', 'hooks', 'aplicacao'],
  app: ['app', 'features', 'aplicacao', 'dominio', 'infra', 'componentes', 'hooks', 'estilos'],
  demonstracao: ['demonstracao', 'features', 'aplicacao', 'dominio', 'infra', 'componentes', 'hooks'],
  estilos: ['estilos'],
}

function arquivos(dir) {
  return readdirSync(dir).flatMap((nome) => {
    const caminho = join(dir, nome)
    if (statSync(caminho).isDirectory()) return arquivos(caminho)
    return /\.(jsx?|mjs)$/.test(caminho) ? [caminho] : []
  })
}

// Arquivo solto na raiz de src (main.jsx) conta como camada de composicao.
const camadaDe = (caminho) => {
  const partes = relative(RAIZ, caminho).split(/[\\/]/)
  return partes.length === 1 ? 'app' : partes[0]
}

const featureDe = (caminho) => {
  const partes = relative(RAIZ, caminho).split(/[\\/]/)
  return partes[0] === 'features' ? partes[1] : null
}

// As unicas portas que podem carregar `src/demonstracao/` (import dinamico).
const PORTAS_DA_DEMONSTRACAO = new Set(['infra/demonstracao.js', 'app/demonstracao.js'])

const faltas = []

for (const arquivo of arquivos(RAIZ)) {
  const camada = camadaDe(arquivo)
  const feature = featureDe(arquivo)
  const codigo = readFileSync(arquivo, 'utf8')
  const importes = [...codigo.matchAll(/from\s+'(\.[^']+)'/g)].map((m) => m[1])

  for (const especificador of importes) {
    const alvo = resolve(dirname(arquivo), especificador)
    if (!alvo.startsWith(RAIZ)) continue
    const camadaAlvo = camadaDe(alvo)
    const featureAlvo = featureDe(alvo)

    if (!(PERMITIDO[camada] ?? []).includes(camadaAlvo)) {
      faltas.push(`${relative('.', arquivo)}: ${camada} nao pode importar de ${camadaAlvo}`)
      continue
    }
    if (feature && featureAlvo && feature !== featureAlvo) {
      faltas.push(`${relative('.', arquivo)}: feature ${feature} importou da feature ${featureAlvo}`)
    }
  }

  // `import('...')`: o pacote da demonstracao so entra pelas portas.
  const caminhoRelativo = relative(RAIZ, arquivo).replaceAll('\\', '/')
  for (const m of codigo.matchAll(/import\(\s*'(\.[^']+)'\s*\)/g)) {
    const alvo = resolve(dirname(arquivo), m[1])
    if (alvo.startsWith(RAIZ) && camadaDe(alvo) === 'demonstracao' && !PORTAS_DA_DEMONSTRACAO.has(caminhoRelativo)) {
      faltas.push(`${relative('.', arquivo)}: so as portas da demonstracao (${[...PORTAS_DA_DEMONSTRACAO].join(', ')}) podem carregar src/demonstracao/`)
    }
  }
}

if (faltas.length > 0) {
  console.error('Fronteira de camada violada:\n' + faltas.map((f) => '  ' + f).join('\n'))
  process.exit(1)
}

console.log('Fronteira de camada: ok, nenhuma violacao.')
