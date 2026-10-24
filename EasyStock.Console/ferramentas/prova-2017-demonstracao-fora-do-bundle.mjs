/* eslint-disable no-console */
// Prova da #2017: o modo demonstração é laboratório local e não vai no bundle de produção.
//   1. grafo: nenhum módulo da demonstração é importado por `import ... from` a partir de `main.jsx` (só as duas
//      portas o carregam, por `import()`), e a lista abaixo é exata: módulo novo da demonstração entra nela;
//   2. ciclo: nada que a demonstração importa importa uma porta (o `await` esperaria por si mesmo);
//   3. bundle: o build de produção (`VITE_FONTE_DADOS=api`) não leva módulo nem texto da demonstração, e o
//      build de demonstração leva (a busca não passa em branco);
//   4. reducer: o do modo API não divide caso com o da demonstração e cobre tudo que o código do modo API despacha;
//   5. ações: com e sem a demonstração, o modo API tem as mesmas ações, e cada ação só da demonstração é trocada;
//   6. demonstração ligada (processo à parte, fora do modo API): reducer, ações e conversas completos.
//
// No `console.yml`, entra depois de `prova-f06-honestidade.mjs`:   - run: node ferramentas/prova-2017-demonstracao-fora-do-bundle.mjs
//
//   node ferramentas/prova-2017-demonstracao-fora-do-bundle.mjs

import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { build } from 'vite'

const raiz = fileURLToPath(new URL('../', import.meta.url))
const src = join(raiz, 'src')
const rel = (arquivo) => relative(src, arquivo).replaceAll('\\', '/')

// Módulos que só a demonstração alcança: fora do grafo de `main.jsx`, dentro do de `demonstracao/{dados,telas}.js`.
const SO_DA_DEMONSTRACAO = [
  'aplicacao/acoes/anexos.js', 'aplicacao/acoes/areaEntrega.js', 'aplicacao/acoes/caixa.js', 'aplicacao/acoes/cardapio.js',
  'aplicacao/acoes/cardapioLink.js', 'aplicacao/acoes/cliente.js', 'aplicacao/acoes/cobranca.js',
  'aplicacao/acoes/demonstracao.js', 'aplicacao/acoes/encerramento.js', 'aplicacao/acoes/fidelidade.js',
  'aplicacao/acoes/funcionamento.js', 'aplicacao/acoes/integracoes.js', 'aplicacao/acoes/janelas.js',
  'aplicacao/acoes/lote.js', 'aplicacao/acoes/producao.js', 'aplicacao/acoes/respostas.js', 'aplicacao/acoes/simulacao.js',
  'aplicacao/casos/anexos.js', 'aplicacao/casos/areaEntrega.js', 'aplicacao/casos/atendimentoLocal.js',
  'aplicacao/casos/caixa.js', 'aplicacao/casos/cardapio.js', 'aplicacao/casos/cardapioLink.js', 'aplicacao/casos/cliente.js',
  'aplicacao/casos/cobranca.js', 'aplicacao/casos/encerramento.js', 'aplicacao/casos/entregas.js',
  'aplicacao/casos/esteira.js', 'aplicacao/casos/fidelidade.js', 'aplicacao/casos/integracoes.js',
  'aplicacao/casos/janelas.js', 'aplicacao/casos/lote.js', 'aplicacao/casos/pagamentoPix.js', 'aplicacao/casos/producao.js',
  'aplicacao/casos/respostas.js', 'aplicacao/casos/simulacao.js',
  'aplicacao/useDigitacaoAutomatica.js', 'aplicacao/useEspelhoCardapioLink.js', 'aplicacao/useEspelhoDeCozinha.js',
  'aplicacao/useEspelhoDeEntregas.js', 'aplicacao/useReacaoClienteSimulado.js',
  'componentes/EscolhaDeProvedorEntrega.jsx',
  'demonstracao/casos.js', 'demonstracao/conversas.js', 'demonstracao/dados.js', 'demonstracao/telas.js',
  'dominio/alcance.js', 'dominio/clienteSimulado.js', 'dominio/corrida.js', 'dominio/resumoDoDia.js', 'dominio/simulacoes.js',
  'features/cardapio-link/TelaCardapioLink.jsx',
  'features/cozinha/CartaoCozinha.jsx', 'features/cozinha/ConteudoCozinha.jsx', 'features/cozinha/TelaCozinha.jsx',
  'features/entregas/CartaoEntrega.jsx', 'features/entregas/ConteudoEntregas.jsx', 'features/entregas/GavetaEntregas.jsx',
  'features/entregas/ModalGerarRota.jsx', 'features/entregas/PainelAlcance.jsx', 'features/entregas/PainelViagem.jsx',
  'features/entregas/ResumoDoDia.jsx', 'features/entregas/TelaEntregas.jsx',
  'features/gestao/caixa/AbaCaixa.jsx', 'features/gestao/caixa/ModalVendaAvulsa.jsx',
  'features/gestao/fidelidade/AbaFidelidade.jsx', 'features/gestao/integracoes/AbaIntegracoes.jsx',
  'features/gestao/janelas/AbaJanelas.jsx', 'features/gestao/producao/AbaProducao.jsx',
  'features/simulacoes/PainelSimulacoes.jsx', 'features/simulacoes/useRoteiro.js',
  'infra/atendimentosAntigos.js', 'infra/avaliacaoSemente.js', 'infra/canalEntreJanelas.js', 'infra/conversasSemente.js',
  'infra/historicoPedidos.js', 'infra/massaClientesGerados.js', 'infra/massaConversas.js', 'infra/provedoresDeEntrega.js',
  'infra/roteirosSimulacao.js',
]
// `infra/api/caixaEmailApi.js` fica de fora: ninguém o alcança (nem a demonstração) e só a prova 1432 o usa.

// Textos que só moram em módulo da demonstração; se aparecerem no bundle de produção, o código voltou.
const TEXTOS_DA_DEMONSTRACAO = [
  'na demonstração não há disparo', // aplicacao/acoes/demonstracao.js
  'cdb-estado-v1', // infra/canalEntreJanelas.js
  'Cadastro rápido salvo pela dona.', // aplicacao/casos/cliente.js
  'Gratinada no forno', // infra/historicoPedidos.js
  'Chegou tudo errado dessa vez', // infra/roteirosSimulacao.js
  'apto 71, Vila Madalena', // dados/massa-conversas.json e infra/conversasSemente.js
]

let passou = 0
const faltas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    faltas.push(descricao)
    console.log('FALHA ' + descricao + '\n      ' + String(erro.message).split('\n').join('\n      '))
  }
}

// ------------------------------------------------------------------------------------------------
// Processo filho: demonstração ligada (fora do modo API), como o desenvolvimento e a imagem de demonstração.
if (process.argv.includes('--demonstracao-ligada')) {
  registerHooks({
    resolve(especificador, contexto, proximo) {
      if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
        try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
      }
      return proximo(especificador, contexto)
    },
    load(url, contexto, proximo) {
      if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = false\nexport const API_BASE = ''\n" }
      if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
      return proximo(url, contexto)
    },
  })
  const { CASOS_COMPOSTOS, CASOS_DO_MODO_API } = await import('../src/aplicacao/reducer.js')
  const { casosDaDemonstracao } = await import('../src/demonstracao/casos.js')
  const { criarAcoes, criarAcoesLocais } = await import('../src/aplicacao/criarAcoes.js')
  const { criarAcoesDaDemonstracao } = await import('../src/aplicacao/acoes/demonstracao.js')
  const { carregarConversas } = await import('../src/infra/repositorioConversas.js')
  const { demonstracao } = await import('../src/infra/demonstracao.js')

  assert.ok(demonstracao, 'fora do modo API a porta carrega a demonstração')
  assert.equal(Object.keys(CASOS_COMPOSTOS).length, Object.keys(CASOS_DO_MODO_API).length + Object.keys(casosDaDemonstracao).length,
    'o reducer da demonstração soma os casos dela aos do modo API')
  for (const chave of Object.keys(casosDaDemonstracao)) assert.equal(CASOS_COMPOSTOS[chave], casosDaDemonstracao[chave], chave)
  const argumentos = {
    despachar: () => {}, agoraRef: { current: 1 }, estadoRef: { current: { conversas: [], catalogo: {} } }, pendentes: { current: [] },
    consultarAgente: async () => {}, perguntarAssistente: async () => '', pedirNotificacaoDoNavegador: async () => {},
  }
  const acoes = criarAcoes(argumentos)
  for (const chave of [...Object.keys(criarAcoesLocais(argumentos)), ...Object.keys(criarAcoesDaDemonstracao(argumentos))]) {
    assert.equal(typeof acoes[chave], 'function', `ação ${chave} na demonstração`)
  }
  const conversas = carregarConversas()
  assert.ok(conversas.length >= 30, `a massa da demonstração carrega (${conversas.length} conversas)`)
  assert.equal(typeof demonstracao.useReacaoClienteSimulado, 'function')
  assert.equal(typeof demonstracao.useDigitacaoAutomatica, 'function')
  assert.equal(typeof demonstracao.conectarPrincipal, 'function')
  assert.deepEqual(demonstracao.carregarHistorico('c1').length > 0, true, 'o histórico da massa chega pela porta')
  console.log(`demonstração ligada: ${Object.keys(CASOS_COMPOSTOS).length} casos, ${Object.keys(acoes).length} ações, ${conversas.length} conversas`)
  process.exit(0)
}

// ------------------------------------------------------------------------------------------------
// 1 e 2. Grafo de imports: `from`, `import 'x'` e `import('x')` (carga preguiçosa, como o CSV do Financeiro). A
// única ponte que o grafo de produção não atravessa é a das portas para `src/demonstracao/`.
const arquivos = readdirSync(src, { recursive: true }).filter((nome) => /\.(jsx?|mjs)$/.test(nome)).map((nome) => join(src, nome))
const existentes = new Set(arquivos)
function resolver(de, especificador) {
  if (!especificador.startsWith('.')) return null
  const base = resolve(dirname(de), especificador)
  for (const extensao of ['', '.js', '.jsx', '/index.js', '/index.jsx']) if (existentes.has(base + extensao)) return base + extensao
  return null
}
const arestas = new Map(arquivos.map((arquivo) => {
  const alvos = new Set()
  const texto = readFileSync(arquivo, 'utf8')
  for (const m of texto.matchAll(/(?:^|\n)\s*(?:import|export)\s[^'"]*?from\s*['"]([^'"]+)['"]|(?:^|\n)\s*import\s*['"]([^'"]+)['"]|\bimport\(\s*['"]([^'"]+)['"]\s*\)/g)) {
    const destino = resolver(arquivo, m[1] ?? m[2] ?? m[3])
    if (destino) alvos.add(destino)
  }
  return [arquivo, alvos]
}))
const ehDaDemonstracao = (arquivo) => rel(arquivo).startsWith('demonstracao/')
const alcance = (entradas, pararNaDemonstracao) => {
  const vistos = new Set(entradas)
  const fila = [...entradas]
  while (fila.length) {
    for (const destino of arestas.get(fila.pop()) ?? []) {
      if (pararNaDemonstracao && ehDaDemonstracao(destino)) continue
      if (!vistos.has(destino)) { vistos.add(destino); fila.push(destino) }
    }
  }
  return vistos
}
const vivos = alcance([join(src, 'main.jsx')], true)
const daDemonstracao = alcance([join(src, 'demonstracao/dados.js'), join(src, 'demonstracao/telas.js')], false)

await confere('nenhum módulo da demonstração é importado por quem roda no build de produção', () => {
  const voltaram = SO_DA_DEMONSTRACAO.filter((modulo) => vivos.has(join(src, modulo)))
  assert.deepEqual(voltaram, [], `entram por import estático a partir de main.jsx: ${voltaram.join(', ')}. Carregue-os por src/demonstracao/ e leia pela porta (infra/demonstracao.js ou app/demonstracao.js).`)
})

await confere('a lista de módulos da demonstração é exata (nada a mais, nada a menos)', () => {
  const inexistentes = SO_DA_DEMONSTRACAO.filter((modulo) => !existentes.has(join(src, modulo)))
  assert.deepEqual(inexistentes, [], `na lista, mas sem arquivo: ${inexistentes.join(', ')}`)
  const sobras = [...daDemonstracao].filter((arquivo) => !vivos.has(arquivo)).map(rel).sort()
  const esperados = [...SO_DA_DEMONSTRACAO].sort()
  assert.deepEqual(sobras.filter((m) => !esperados.includes(m)), [], 'só a demonstração alcança e não está na lista: acrescente em SO_DA_DEMONSTRACAO')
  assert.deepEqual(esperados.filter((m) => !sobras.includes(m)), [], 'está na lista, mas a demonstração não o alcança (ou o build de produção também)')
})

await confere('nada que a demonstração importa importa uma porta (o await de módulo esperaria por si mesmo)', () => {
  const portas = new Set(['infra/demonstracao.js', 'app/demonstracao.js'].map((modulo) => join(src, modulo)))
  const ciclos = [...daDemonstracao].filter((arquivo) => [...(arestas.get(arquivo) ?? [])].some((destino) => portas.has(destino))).map(rel)
  assert.deepEqual(ciclos, [], `importam uma porta: ${ciclos.join(', ')}`)
})

// ------------------------------------------------------------------------------------------------
// 3. Bundle. O build roda em memória; `minify: false` deixa o `//#region src/...` de cada módulo.
async function compilar({ api, minify }) {
  const anterior = process.env.VITE_FONTE_DADOS
  if (api) process.env.VITE_FONTE_DADOS = 'api'
  else delete process.env.VITE_FONTE_DADOS
  try {
    const saida = await build({ root: raiz, logLevel: 'silent', build: { write: false, minify, reportCompressedSize: false } })
    const html = (Array.isArray(saida) ? saida[0] : saida).output.find((item) => item.fileName === 'index.html')
    return String(html.source)
  } finally {
    if (anterior === undefined) delete process.env.VITE_FONTE_DADOS
    else process.env.VITE_FONTE_DADOS = anterior
  }
}
const regioes = (html) => new Set([...html.matchAll(/\/\/#region (src\/[^\r\n]+)/g)].map((m) => m[1].trim().replace(/^src\//, '')))

let producao
let producaoMinificada
let demonstracaoCompleta
await confere('o build compila nos dois modos', async () => {
  producao = await compilar({ api: true, minify: false })
  producaoMinificada = await compilar({ api: true, minify: true })
  demonstracaoCompleta = await compilar({ api: false, minify: false })
  assert.ok(producao.length > 100_000 && demonstracaoCompleta.length > 100_000, 'o html único saiu vazio')
})

await confere('o bundle de produção (VITE_FONTE_DADOS=api) não leva módulo da demonstração', () => {
  const dentro = regioes(producao)
  assert.ok(dentro.size > 200, `só ${dentro.size} módulos no bundle: o marcador //#region sumiu e a busca passaria em branco`)
  const vazaram = SO_DA_DEMONSTRACAO.filter((modulo) => dentro.has(modulo))
  assert.deepEqual(vazaram, [], `no bundle de produção: ${vazaram.join(', ')}`)
})

await confere('o bundle de produção, minificado, não leva texto da demonstração', () => {
  const achados = TEXTOS_DA_DEMONSTRACAO.filter((texto) => producaoMinificada.includes(texto))
  assert.deepEqual(achados, [], `no bundle de produção: ${achados.join(' | ')}`)
})

await confere('o bundle de demonstração leva os módulos e os textos dela (a busca enxerga o que procura)', () => {
  const dentro = regioes(demonstracaoCompleta)
  const faltando = ['aplicacao/casos/esteira.js', 'aplicacao/acoes/demonstracao.js', 'infra/conversasSemente.js', 'infra/massaConversas.js',
    'features/cozinha/TelaCozinha.jsx', 'features/entregas/TelaEntregas.jsx', 'features/gestao/producao/AbaProducao.jsx'].filter((modulo) => !dentro.has(modulo))
  assert.deepEqual(faltando, [], `a demonstração perdeu: ${faltando.join(', ')}`)
  const semTexto = TEXTOS_DA_DEMONSTRACAO.filter((texto) => !demonstracaoCompleta.includes(texto))
  assert.deepEqual(semTexto, [], `texto que não está mais na demonstração: ${semTexto.join(' | ')}`)
})

// ------------------------------------------------------------------------------------------------
// 4 e 5. Modo API no node: `FONTE_API` fixo em `true`, a demonstração não é carregada.
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})
globalThis.sessionStorage = { getItem: () => null, setItem() {}, removeItem() {} }
globalThis.window = { dispatchEvent: () => {}, location: { origin: 'http://x', pathname: '/' } }

const importar = (caminho) => import(pathToFileURL(join(src, caminho)).href)
const acao = await importar('aplicacao/acoes.js')
const { CASOS_COMPOSTOS, CASOS_DO_MODO_API } = await importar('aplicacao/reducer.js')
const { casosDaDemonstracao } = await importar('demonstracao/casos.js')
const { criarAcoes } = await importar('aplicacao/criarAcoes.js')
const { criarAcoesDaDemonstracao } = await importar('aplicacao/acoes/demonstracao.js')
const { comApi } = await importar('aplicacao/acoesApi.js')
const { SO_DA_TELA } = await importar('aplicacao/api/naoLigadas.js')
const { demonstracao } = await importar('infra/demonstracao.js')

await confere('no modo API a porta devolve nulo e o reducer é só o do modo API', () => {
  assert.equal(demonstracao, null)
  assert.equal(CASOS_COMPOSTOS, CASOS_DO_MODO_API)
})

await confere('o reducer do modo API e o da demonstração não dividem caso', () => {
  const repetidos = Object.keys(casosDaDemonstracao).filter((chave) => chave in CASOS_DO_MODO_API)
  assert.deepEqual(repetidos, [], `nos dois: ${repetidos.join(', ')}`)
})

await confere('toda ação que o código do modo API despacha tem caso no reducer do modo API', () => {
  const constantes = Object.keys(acao)
  const despachantes = [
    'aplicacao/acoesApi.js', 'aplicacao/AtendimentoProvider.jsx', 'aplicacao/useSincronizacaoApi.js',
    'aplicacao/cicloDeSincronizacao.js', 'aplicacao/comandasDaVisita.js', 'aplicacao/useLembretesApi.js',
    'aplicacao/criarAcoes.js', 'aplicacao/acoes/locais.js',
    ...readdirSync(join(src, 'aplicacao/api')).map((nome) => `aplicacao/api/${nome}`),
  ]
  // O Provider só despacha ENCERRAR_POR_JANELA fora do modo API (`if (FONTE_API) return`).
  const SO_FORA_DO_MODO_API = new Set(['ENCERRAR_POR_JANELA'])
  const sem = []
  for (const arquivo of despachantes) {
    const texto = readFileSync(join(src, arquivo), 'utf8')
    for (const m of texto.matchAll(/\bacao\.([A-Z_0-9]+)/g)) {
      if (constantes.includes(m[1]) && !SO_FORA_DO_MODO_API.has(m[1]) && !(acao[m[1]] in CASOS_DO_MODO_API)) sem.push(`${arquivo}: ${m[1]}`)
    }
  }
  assert.deepEqual([...new Set(sem)], [], 'despachada no modo API, sem caso no reducer do modo API (ficou só na demonstração?)')
})

await confere('o modo API tem as mesmas ações com e sem a demonstração, e cada ação só da demonstração é trocada', () => {
  const argumentos = {
    despachar: () => {}, agoraRef: { current: 1 }, estadoRef: { current: { conversas: [], catalogo: { cardapio: [], janelas: [], canais: [] } } },
    pendentes: { current: [] }, consultarAgente: async () => {}, perguntarAssistente: async () => '', pedirNotificacaoDoNavegador: async () => {},
  }
  const contexto = { despachar: () => {}, agoraRef: { current: 1 }, estadoRef: argumentos.estadoRef }
  const soApi = criarAcoes(argumentos)
  const daDemonstracao = criarAcoesDaDemonstracao(argumentos)
  const comDemonstracao = { ...soApi, ...daDemonstracao }
  const apiSem = comApi(soApi, contexto)
  const apiCom = comApi(comDemonstracao, contexto)
  assert.deepEqual(Object.keys(apiSem).sort(), Object.keys(apiCom).sort(), 'o objeto de ações do modo API mudou sem a demonstração')
  const daTela = new Set(SO_DA_TELA)
  const mantidas = Object.keys(daDemonstracao).filter((chave) => daTela.has(chave) || apiSem[chave] === undefined)
  assert.deepEqual(mantidas, [], `ação da demonstração que o modo API usa sem trocar (ponha em acoes/locais.js): ${mantidas.join(', ')}`)
  for (const chave of Object.keys(daDemonstracao)) assert.notEqual(apiCom[chave], daDemonstracao[chave], `${chave} da demonstração vaza para o modo API`)
  for (const chave of daTela) assert.equal(typeof soApi[chave], 'function', `"só da tela" ${chave} precisa existir sem a demonstração`)
})

// ------------------------------------------------------------------------------------------------
// 6. Demonstração ligada, em outro processo (o `FONTE_API` do módulo carregado acima já está fixo).
await confere('com a demonstração ligada, reducer, ações e conversas estão completos', () => {
  const filho = spawnSync(process.execPath, [fileURLToPath(import.meta.url), '--demonstracao-ligada'], { encoding: 'utf8' })
  assert.equal(filho.status, 0, `${filho.stdout}\n${filho.stderr}`)
  console.log('      ' + filho.stdout.trim())
})

console.log(`\n${passou} verificações passaram, ${faltas.length} falharam.`)
if (faltas.length) process.exit(1)
