/* eslint-disable no-console */
// Roda todas as provas `ferramentas/prova-*.mjs` (descobertas por glob, #2391), cada uma num processo Node
// filho. Prova nova entra sozinha: não há lista a manter à mão, só a de EXCEÇÕES abaixo.
//
//   npm run provas                    todas, com PROVAS_PARALELO processos (padrão 4)
//   node ferramentas/rodar-provas.mjs 2017 r12   só as que casam com os termos
//
// Exceção é prova que NÃO roda aqui; cada uma leva o motivo em uma linha. Prova que falha e não está na lista
// derruba o script. Não conserte prova alheia aqui: marque, abra issue e tire da lista quando passar.
import { spawn } from 'node:child_process'
import { readdirSync } from 'node:fs'
import { cpus } from 'node:os'
import { fileURLToPath } from 'node:url'

const EXCECOES = new Map([
  ['prova-1653-experiencia-navegador.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1653-teclado-navegador.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1685-sinal-navegador.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1693-dez-cenarios-api-real.mjs', 'lê .build/logistica-browser.json gerado por outra prova de navegador (Playwright e API real)'],
  ['prova-1693-frete-api-real.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1693-frete-navegador.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1712-comanda-operador.mjs', 'precisa de DOM (document) que o Node puro não tem (já falha no master)'],
  ['prova-1722-canais-navegador.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1728-teste-email-navegador.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1733-conferencia-navegador.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1738-pedido-navegador.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1739-pedido-final.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1742-chat-recepcao.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1743-tamanhos-navegador.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1747-acerto-cancelado.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1748-chat-comanda.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1749-sinal-navegador.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1753-usuarios.mjs', 'asserção velha, já falha no master (a tela mudou e a prova não acompanhou)'],
  ['prova-1757-complemento-navegador.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1759-producao-sprint.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1839-balcao.mjs', 'asserção velha, já falha no master (a tela mudou e a prova não acompanhou)'],
  ['prova-1841-cardapio-acervo.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1846-seletor-acervo.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1847-nota-resumo.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-1868-visual.mjs', 'asserção velha, já falha no master (a tela mudou e a prova não acompanhou)'],
  ['prova-1920-tela-real.mjs', 'asserção velha, já falha no master (a tela mudou e a prova não acompanhou)'],
  ['prova-2096-avisos-operacao.mjs', 'asserção velha, já falha no master (a tela mudou e a prova não acompanhou)'],
  ['prova-2134-assumir-ao-responder.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-2145-icones-lucide.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-2178-editor-nota.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-2185-efeitos-tela.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-2185-menu-de-acoes.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-2256-onda1-ux.mjs', 'asserção velha, já falha no master (a tela mudou e a prova não acompanhou)'],
  ['prova-bloqueio-preparo.mjs', 'importa src/ sem o hook do Vite e quebra em import.meta.env (já falha no master)'],
  ['prova-contadores-canal.mjs', 'asserção velha, já falha no master (a tela mudou e a prova não acompanhou)'],
  ['prova-financeiro-navegador.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
  ['prova-hub-navegador.mjs', 'precisa do pacote playwright, que o npm ci não instala'],
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

const rodar = (nome) => new Promise((resolver) => {
  const inicio = Date.now()
  const filho = spawn(process.execPath, [`${pasta}${nome}`], { cwd: `${pasta}..`, stdio: ['ignore', 'pipe', 'pipe'] })
  let saida = ''
  filho.stdout.on('data', (parte) => { saida += parte })
  filho.stderr.on('data', (parte) => { saida += parte })
  const relogio = setTimeout(() => { saida += `\n[estourou ${TETO_MS / 1000}s]`; filho.kill() }, TETO_MS)
  filho.on('close', (codigo) => { clearTimeout(relogio); resolver({ nome, codigo, saida, ms: Date.now() - inicio }) })
})

const inicio = Date.now()
const falhas = []
const fila = [...aRodar]
await Promise.all(Array.from({ length: paralelo }, async () => {
  while (fila.length > 0) {
    const resultado = await rodar(fila.shift())
    if (resultado.codigo === 0) console.log(`ok    ${resultado.nome} (${(resultado.ms / 1000).toFixed(1)}s)`)
    else { falhas.push(resultado); console.log(`FALHA ${resultado.nome} (código ${resultado.codigo})`) }
  }
}))

for (const falha of falhas) console.log(`\n--- ${falha.nome} ---\n${falha.saida.split('\n').slice(-25).join('\n')}`)
console.log(`\nprovas: ${descobertas.length} descobertas, ${aRodar.length - falhas.length} ok, ${falhas.length} falharam, ${excetuadas.length} excetuadas, ${((Date.now() - inicio) / 1000).toFixed(0)}s`)
for (const nome of excetuadas) console.log(`  exceção ${nome}: ${EXCECOES.get(nome)}`)
process.exit(falhas.length > 0 ? 1 : 0)
