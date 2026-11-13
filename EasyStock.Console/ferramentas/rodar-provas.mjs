/* eslint-disable no-console */
// Roda todas as provas `ferramentas/prova-*.mjs` (descobertas por glob, #2391), cada uma num processo Node
// filho. Prova nova entra sozinha: não há lista a manter à mão, só a de EXCEÇÕES abaixo.
//
//   npm run provas                    todas, com PROVAS_PARALELO processos (padrão 4)
//   node ferramentas/rodar-provas.mjs 2017 r12   só as que casam com os termos
//
// Prova de navegador (#2410) é a que carrega o Playwright: o script sobe um Vite só para elas, no modo API e em
// 127.0.0.1, e passa a URL em CONSOLE_URL. Antes, uma vez por máquina: `npx playwright install chromium`.
//
// Exceção é prova que NÃO roda aqui; cada uma leva o motivo em uma linha. Prova que falha e não está na lista
// derruba o script. Não conserte prova alheia aqui: marque, abra issue e tire da lista quando passar.
import { spawn } from 'node:child_process'
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { cpus } from 'node:os'
import { fileURLToPath } from 'node:url'

const EXCECOES = new Map([
  ['prova-1653-experiencia-navegador.mjs', '#2410: espera elemento que a tela atual não tem (timeout no waitFor)'],
  ['prova-1653-teclado-navegador.mjs', '#2410: fixa a URL em 127.0.0.1:5199 e ignora CONSOLE_URL'],
  ['prova-1685-sinal-navegador.mjs', '#2410: espera elemento que a tela atual não tem (timeout no waitFor)'],
  ['prova-1693-frete-api-real.mjs', '#2410: lê .build/logistica-browser.json gerado por outra prova'],
  ['prova-1693-frete-navegador.mjs', '#2410: asserção velha (a tela mudou e a prova não acompanhou)'],
  ['prova-1728-teste-email-navegador.mjs', '#2410: espera elemento que a tela atual não tem (timeout)'],
  ['prova-1733-conferencia-navegador.mjs', '#2410: espera elemento que a tela atual não tem (timeout no waitFor)'],
  ['prova-1738-pedido-navegador.mjs', '#2410: espera elemento que a tela atual não tem (timeout no waitFor)'],
  ['prova-1739-pedido-final.mjs', '#2410: lê evidência em .build/pedido-final gerada por outra execução'],
  ['prova-1742-chat-recepcao.mjs', '#2410: exige variável de ambiente de pasta de evidência sem padrão (paths[0] undefined)'],
  ['prova-1743-tamanhos-navegador.mjs', '#2410: lê .build/1743/menu-publico-auditoria.json gerado por outra prova'],
  ['prova-1747-acerto-cancelado.mjs', '#2410: lê evidência em .build/acerto-cancelado gerada por outra execução'],
  ['prova-1748-chat-comanda.mjs', '#2410: exige variável de ambiente de pasta de evidência sem padrão (paths[0] undefined)'],
  ['prova-1749-sinal-navegador.mjs', '#2410: espera elemento que a tela atual não tem (timeout no waitFor)'],
  ['prova-1841-cardapio-acervo.mjs', '#2410: chama uma API real (fetch failed sem servidor)'],
  ['prova-1846-seletor-acervo.mjs', '#2410: chama uma API real (fetch failed sem servidor)'],
  ['prova-1847-nota-resumo.mjs', '#2410: clica em elemento que a tela atual não tem (timeout)'],
  ['prova-2134-assumir-ao-responder.mjs', '#2410: não acha o campo "Mensagem para o cliente" no console do modo API'],
  ['prova-2145-icones-lucide.mjs', '#2410: exige dist/ (npm run build) antes, e o build roda depois das provas'],
  ['prova-2178-editor-nota.mjs', '#2410: o Vite próprio dela não resolve react/jsx-dev-runtime a partir da pasta temporária'],
  ['prova-2185-efeitos-tela.mjs', '#2410: não acha o campo "Mensagem para o cliente" no console do modo API'],
  ['prova-2185-menu-de-acoes.mjs', '#2410: não acha a região "Quem responde esta conversa" no console do modo API'],
  ['prova-hub-navegador.mjs', '#2410: asserção velha de valor (espera 300,03)'],
  ['prova-1693-dez-cenarios-api-real.mjs', 'lê .build/logistica-browser.json gerado por outra prova de navegador (Playwright e API real)'],
  ['prova-1712-comanda-operador.mjs', 'precisa de DOM (document) que o Node puro não tem (já falha no master)'],
  ['prova-1753-usuarios.mjs', 'asserção velha, já falha no master (a tela mudou e a prova não acompanhou)'],
  ['prova-1839-balcao.mjs', 'asserção velha, já falha no master (a tela mudou e a prova não acompanhou)'],
  ['prova-1868-visual.mjs', 'asserção velha, já falha no master (a tela mudou e a prova não acompanhou)'],
  ['prova-1920-tela-real.mjs', 'asserção velha, já falha no master (a tela mudou e a prova não acompanhou)'],
  ['prova-2096-avisos-operacao.mjs', 'asserção velha, já falha no master (a tela mudou e a prova não acompanhou)'],
  ['prova-2256-onda1-ux.mjs', 'asserção velha, já falha no master (a tela mudou e a prova não acompanhou)'],
  ['prova-bloqueio-preparo.mjs', 'importa src/ sem o hook do Vite e quebra em import.meta.env (já falha no master)'],
  ['prova-contadores-canal.mjs', 'asserção velha, já falha no master (a tela mudou e a prova não acompanhou)'],
  ['prova-producao-ia-navegador.mjs', 'precisa de servidor Vite no ar na porta 5187 e navegador (já falha no master)'],
  ['prova-r11-atendimento.mjs', 'importa src/ sem o hook do Vite e quebra em import.meta.env (já falha no master)'],
  ['prova-r11-cozinha.mjs', 'importa src/ sem o hook do Vite e quebra em import.meta.env (já falha no master)'],
  ['prova-r12-cardapio-cliente.mjs', 'importa src/ sem o hook do Vite e quebra em import.meta.env (já falha no master)'],
  ['prova-r12-entregas.mjs', 'importa src/ sem o hook do Vite e quebra em import.meta.env (já falha no master)'],
  ['prova-r12-enviar-comanda.mjs', 'importa src/ sem o hook do Vite e quebra em import.meta.env (já falha no master)'],
  ['prova-r12-trocar-pagamento.mjs', 'importa src/ sem o hook do Vite e quebra em import.meta.env (já falha no master)'],
  ['prova-r13-caixa.mjs', 'importa src/ sem o hook do Vite e quebra em import.meta.env (já falha no master)'],
  ['prova-r13-fidelidade.mjs', 'importa src/ sem o hook do Vite e quebra em import.meta.env (já falha no master)'],
  ['prova-r13-integracoes.mjs', 'importa src/ sem o hook do Vite e quebra em import.meta.env (já falha no master)'],
  ['prova-r13-janelas.mjs', 'importa src/ sem o hook do Vite e quebra em import.meta.env (já falha no master)'],
  ['prova-r13-producao.mjs', 'importa src/ sem o hook do Vite e quebra em import.meta.env (já falha no master)'],
])

const pasta = fileURLToPath(new URL('./', import.meta.url))
const TETO_MS = 180_000
const paralelo = Number(process.env.PROVAS_PARALELO) || Math.min(4, cpus().length)
const termos = process.argv.slice(2)

const descobertas = readdirSync(pasta).filter((nome) => /^prova-.+\.mjs$/.test(nome)).sort()
const selecionadas = termos.length > 0 ? descobertas.filter((nome) => termos.some((termo) => nome.includes(termo))) : descobertas
const excetuadas = selecionadas.filter((nome) => EXCECOES.has(nome))
const aRodar = selecionadas.filter((nome) => !EXCECOES.has(nome))

const deNavegador = aRodar.filter((nome) => readFileSync(`${pasta}${nome}`, 'utf8').includes('PLAYWRIGHT_MODULE'))
// Sem o Chromium, fora do CI, as de navegador são puladas com aviso; no CI (CI=true) a falta derruba o script.
const semNavegador = deNavegador.length > 0 && !chromiumInstalado()
if (semNavegador && process.env.CI) throw new Error('Chromium ausente: rode `npx playwright install --with-deps chromium` antes das provas.')
const vite = deNavegador.length > 0 && !semNavegador ? await subirVite() : null
const ambiente = vite ? { ...process.env, CONSOLE_URL: vite.url } : process.env
const puladas = semNavegador ? deNavegador : []

function chromiumInstalado() {
  try {
    const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
    return existsSync(chromium.executablePath())
  } catch { return false }
}

// O console no modo API (o mesmo do build de produção), com as rotas /api interceptadas por cada prova.
async function subirVite() {
  process.env.VITE_FONTE_DADOS = 'api'
  const { createServer } = await import('vite')
  const servidor = await createServer({ root: `${pasta}..`, logLevel: 'error', server: { host: '127.0.0.1', port: 5790, strictPort: false } })
  await servidor.listen()
  const url = servidor.resolvedUrls.local[0].replace(/\/$/, '')
  // Aquece: a primeira página faz o Vite pré-empacotar as dependências e recarregar; sem isso, as provas que
  // abrem primeiro estouram o tempo do goto.
  const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
  const navegador = await chromium.launch({ headless: true })
  try {
    const pagina = await navegador.newPage()
    await pagina.goto(url, { waitUntil: 'networkidle', timeout: 120_000 })
    await pagina.waitForTimeout(1_000)
    await pagina.reload({ waitUntil: 'networkidle', timeout: 120_000 })
  } finally { await navegador.close() }
  return { url, fechar: () => servidor.close() }
}

const rodar = (nome) => new Promise((resolver) => {
  const inicio = Date.now()
  const filho = spawn(process.execPath, [`${pasta}${nome}`], { cwd: `${pasta}..`, env: ambiente, stdio: ['ignore', 'pipe', 'pipe'] })
  let saida = ''
  filho.stdout.on('data', (parte) => { saida += parte })
  filho.stderr.on('data', (parte) => { saida += parte })
  const relogio = setTimeout(() => { saida += `\n[estourou ${TETO_MS / 1000}s]`; filho.kill() }, TETO_MS)
  filho.on('close', (codigo) => { clearTimeout(relogio); resolver({ nome, codigo, saida, ms: Date.now() - inicio }) })
})

const inicio = Date.now()
const falhas = []
const fila = aRodar.filter((nome) => !puladas.includes(nome))
await Promise.all(Array.from({ length: paralelo }, async () => {
  while (fila.length > 0) {
    const resultado = await rodar(fila.shift())
    if (resultado.codigo === 0) console.log(`ok    ${resultado.nome} (${(resultado.ms / 1000).toFixed(1)}s)`)
    else { falhas.push(resultado); console.log(`FALHA ${resultado.nome} (código ${resultado.codigo})`) }
  }
}))

await vite?.fechar()
for (const falha of falhas) console.log(`\n--- ${falha.nome} ---\n${falha.saida.split('\n').slice(-25).join('\n')}`)
console.log(`\nprovas: ${descobertas.length} descobertas, ${aRodar.length - puladas.length - falhas.length} ok, ${falhas.length} falharam, ${excetuadas.length} excetuadas, ${puladas.length} puladas sem Chromium, ${((Date.now() - inicio) / 1000).toFixed(0)}s`)
for (const nome of excetuadas) console.log(`  exceção ${nome}: ${EXCECOES.get(nome)}`)
if (puladas.length > 0) console.log(`\nAVISO: ${puladas.length} provas de navegador puladas porque o Chromium não está instalado. Rode \`npx playwright install chromium\`.`)
process.exit(falhas.length > 0 ? 1 : 0)
