import { pathToFileURL } from 'node:url'

// Endereços e versões são explícitos: um build verde não prova que houve deploy.
export async function verificarHttp({ api, console: consoleUrl, apiSha, consoleSha, timeoutMs = 10000 }) {
  for (const [nome, url] of Object.entries({ api, console: consoleUrl })) {
    const endereco = new URL(url)
    if (!['http:', 'https:'].includes(endereco.protocol) || endereco.username || endereco.password)
      throw new Error(`${nome}: URL HTTP(S) sem credenciais obrigatória`)
  }
  for (const sha of [apiSha, consoleSha]) {
    if (!/^[a-f0-9]{40}$/i.test(sha ?? '')) throw new Error('Informe os SHAs completos da API e do console')
  }
  async function ler(url) {
    const resposta = await fetch(url, { redirect: 'error', signal: AbortSignal.timeout(timeoutMs) })
    if (resposta.status !== 200) throw new Error(`${url}: HTTP ${resposta.status}, esperado 200`)
    return resposta
  }
  const baseApi = api.replace(/\/$/, '')
  await ler(`${baseApi}/health/ready`)
  const versao = await (await ler(`${baseApi}/health/version`)).json()
  if (versao.buildSha !== apiSha) throw new Error(`API com versão diferente: ${versao.buildSha}`)
  const html = await (await ler(consoleUrl)).text()
  const meta = (html.match(/<meta\b[^>]*>/gi) ?? []).find(m => /name=["']easystok-console-sha["']/i.test(m))
  const publicada = meta?.match(/content=["']([^"']+)["']/i)?.[1]
  if (publicada !== consoleSha) throw new Error(`Console com versão diferente ou ausente: ${publicada ?? 'sem marcador'}`)
  return { apiSha, consoleSha, pronta: true }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  verificarHttp({ api: process.env.HOMOLOGACAO_API_URL, console: process.env.HOMOLOGACAO_CONSOLE_URL,
    apiSha: process.env.HOMOLOGACAO_API_SHA, consoleSha: process.env.HOMOLOGACAO_CONSOLE_SHA })
    .then(resultado => console.log(JSON.stringify(resultado)))
    .catch(erro => { console.error(`Homologação reprovada: ${erro.message}`); process.exitCode = 1 })
}
