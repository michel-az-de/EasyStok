/* eslint-disable no-console */
// Servidor local do agente da Casa da Baba. Sem framework: http + child_process.
// O console é a interface deste processo: cada chamada sai numa linha de log.
//
//   POST /api/agente  { prompt, transporte: "cli" | "api" }
//     -> { texto, modelo, tokens, tokensSaida, custoUsd, latenciaMs, transporte }
//   GET  /api/saude   -> { ok, modo, cli, api, modeloCli, modeloApi }
//   GET  /...         -> arquivos de dist/ (depois de `npm run build`)
//
// cli: roda `claude -p` com o mesmo login do Claude Code, sem chave.
// api: SDK oficial da Anthropic; precisa de ANTHROPIC_API_KEY no ambiente.
// falso (AGENTE_MODO=falso): sem rede e sem processo filho, para subir em
// container de teste sem cli nem chave disponíveis. Responde os dois
// transportes com texto determinístico que sempre avisa, no fim do texto,
// que é simulado. Fora deste modo, cli e api continuam exatamente como estão.
//
// Rodar: node servidor/agente.mjs   (ou npm run agente / npm run local)
// Variáveis: PORTA_AGENTE (5245), HOST_AGENTE (127.0.0.1), MODELO_CLI, MODELO_API,
//            AGENTE_MODO (vazio; "falso" liga o modo acima).

import http from 'node:http'
import { spawn } from 'node:child_process'
import { readFile } from 'node:fs/promises'
import { existsSync, mkdirSync } from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import Anthropic from '@anthropic-ai/sdk'

const AQUI = path.dirname(fileURLToPath(import.meta.url))
const DIST = path.resolve(AQUI, '..', 'dist')
const PASTA_NEUTRA = path.join(os.tmpdir(), 'casa-da-baba-agente')
mkdirSync(PASTA_NEUTRA, { recursive: true })
const PORTA = Number(process.env.PORTA_AGENTE ?? 5245)
const HOST = process.env.HOST_AGENTE ?? '127.0.0.1'
const MODELO_CLI = process.env.MODELO_CLI ?? null // null: o padrão do próprio Claude Code
const MODELO_API = process.env.MODELO_API ?? 'claude-opus-5'
const MODO_FALSO = process.env.AGENTE_MODO === 'falso'
const LIMITE_CORPO = 64 * 1024
// Achado P0 rodada 9: 90 s era tempo demais (a tela promete nunca ficar
// presa, teto combinado com a banca é 25 s ponta a ponta). 15 s cobre a
// resposta saudável (5 a 12 s medido) com folga e ainda deixa margem para o
// timeout do cliente (conexaoAgente.js) e para a rede.
const TEMPO_MAXIMO_MS = 15_000

const TIPOS = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript',
  '.css': 'text/css',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.json': 'application/json',
  '.woff2': 'font/woff2',
}

function lerCorpo(req) {
  return new Promise((resolver, rejeitar) => {
    let dados = ''
    req.on('data', (parte) => {
      dados += parte
      if (dados.length > LIMITE_CORPO) {
        rejeitar(new Error('corpo acima de 64 KB'))
        req.destroy()
      }
    })
    req.on('end', () => {
      try {
        resolver(dados ? JSON.parse(dados) : {})
      } catch {
        rejeitar(new Error('corpo não é JSON'))
      }
    })
    req.on('error', rejeitar)
  })
}

// System prompt curto e fixo. Sem ele o `claude -p` carrega o prompt padrão do
// Claude Code, com CLAUDE.md, memória e skills: medido em 22/09/2026, 23 mil
// tokens e 40 s por resposta. Com ele, 7,5 mil tokens e 5 s. As instruções de
// verdade continuam no texto do usuário, que a dona enxerga na tela.
const SISTEMA_CLI = 'Você é o agente de atendimento da Casa da Baba, massa artesanal em São Paulo. '
  + 'Siga à risca as instruções e as travas do texto que recebe e responda somente com o JSON pedido, '
  + 'sem texto em volta.'

// Transporte 1: a linha de comando do Claude Code, em modo não interativo.
function porCli(prompt) {
  return new Promise((resolver, rejeitar) => {
    const inicio = Date.now()
    const args = [
      '-p', '--no-session-persistence', '--output-format', 'json', '--tools', '',
      '--system-prompt', SISTEMA_CLI, '--exclude-dynamic-system-prompt-sections',
    ]
    if (MODELO_CLI) args.push('--model', MODELO_CLI)
    const env = { ...process.env }
    // Achado P0 rodada 9: apagar só CLAUDECODE não bastava. Rodando este
    // servidor de dentro de uma sessão do Claude Code (harness deste próprio
    // projeto, fleet com várias sessões concorrentes), sobram
    // CLAUDE_CODE_SESSION_ID, CLAUDE_CODE_MESSAGING_SOCKET e outras variáveis
    // de sessão; o `claude -p` filho as herda e tenta coordenar com uma
    // sessão pai que não é a dele, e nunca fecha a resposta. Medido: com só
    // CLAUDECODE apagada, o filho trava os 90 s inteiros até o timeout matar
    // ele; apagando todo prefixo CLAUDE, sai sozinho em segundos.
    for (const chave of Object.keys(env)) {
      if (chave.startsWith('CLAUDE')) delete env[chave]
    }
    // Pasta vazia como cwd: senão o claude -p carrega CLAUDE.md e memória do
    // projeto de onde o servidor foi iniciado (medido: 17 mil tokens a mais).
    const filho = spawn('claude', args, { env, cwd: PASTA_NEUTRA, windowsHide: true })
    let saida = ''
    let erro = ''
    const relogio = setTimeout(() => {
      filho.kill()
      rejeitar(new Error(`claude -p passou de ${TEMPO_MAXIMO_MS / 1000} s`))
    }, TEMPO_MAXIMO_MS)
    filho.stdout.on('data', (d) => { saida += d })
    filho.stderr.on('data', (d) => { erro += d })
    filho.on('error', (e) => {
      clearTimeout(relogio)
      rejeitar(new Error('não achei o comando claude: ' + e.message))
    })
    filho.on('close', () => {
      clearTimeout(relogio)
      let json
      try {
        json = JSON.parse(saida)
      } catch {
        rejeitar(new Error('claude -p não devolveu JSON: ' + (erro || saida).slice(0, 200)))
        return
      }
      if (json.is_error) {
        rejeitar(new Error('claude -p: ' + json.result))
        return
      }
      const [modelo, uso] = Object.entries(json.modelUsage ?? {})[0] ?? [MODELO_CLI ?? 'claude', null]
      resolver({
        texto: json.result ?? '',
        modelo,
        tokens: uso
          ? uso.inputTokens + uso.cacheReadInputTokens + uso.cacheCreationInputTokens + uso.outputTokens
          : null,
        tokensSaida: uso?.outputTokens ?? null,
        custoUsd: json.total_cost_usd ?? null,
        latenciaMs: Date.now() - inicio,
        transporte: 'cli',
      })
    })
    filho.stdin.end(prompt)
  })
}

// Transporte 2: a API da Anthropic pelo SDK oficial. O cliente sem argumento
// resolve a credencial do ambiente (ANTHROPIC_API_KEY ou perfil do `ant auth login`).
let clienteApi = null
function obterClienteApi() {
  if (clienteApi) return clienteApi
  try {
    clienteApi = new Anthropic()
  } catch {
    throw new Error('modo api sem credencial: defina ANTHROPIC_API_KEY no ambiente do servidor')
  }
  return clienteApi
}

async function porApi(prompt) {
  const cliente = obterClienteApi()
  const inicio = Date.now()
  try {
    const resposta = await cliente.messages.create({
      model: MODELO_API,
      max_tokens: 1024, // resposta curta de WhatsApp, por desenho
      output_config: { effort: 'low' },
      messages: [{ role: 'user', content: prompt }],
    })
    if (resposta.stop_reason === 'refusal') throw new Error('o modelo recusou responder')
    const texto = resposta.content.filter((b) => b.type === 'text').map((b) => b.text).join('')
    const uso = resposta.usage
    return {
      texto,
      modelo: resposta.model,
      tokens: uso.input_tokens + uso.output_tokens
        + (uso.cache_read_input_tokens ?? 0) + (uso.cache_creation_input_tokens ?? 0),
      tokensSaida: uso.output_tokens,
      custoUsd: null,
      latenciaMs: Date.now() - inicio,
      transporte: 'api',
    }
  } catch (e) {
    if (e instanceof Anthropic.AuthenticationError) throw new Error('API: credencial inválida')
    if (e instanceof Anthropic.RateLimitError) throw new Error('API: limite de requisições, tente em instantes')
    if (e instanceof Anthropic.APIConnectionError) throw new Error('API: sem conexão com api.anthropic.com')
    if (e instanceof Anthropic.APIError) throw new Error('API: ' + e.message)
    throw e
  }
}

// Transporte 3: modo falso. Sem rede, sem processo filho. A mesma rota
// atende dois formatos de prompt (achado ao ler dominio/agente.js e
// dominio/assistente.js do front): o rascunho de resposta ao cliente exige
// o contrato JSON de montarPrompt ("Responda SOMENTE com um JSON..."), a
// pergunta livre do assistente (montarPromptLivre) não tem esse contrato e
// espera texto solto. Como os dois chegam pelo mesmo POST, a distinção é
// pela própria marca do prompt.
const AVISO_SIMULADO = 'resposta simulada, sem chamada real ao modelo'
// "Na hora" não é 0 ms: a prova prova-r9-assistente confere "Pensando…" 300 ms
// depois de perguntar. Um atraso menor que isso faria a resposta chegar antes
// da checagem existir. 400 ms cobre essa janela e fica bem abaixo do teto de
// 1 s combinado para a prova do container.
const ATRASO_FALSO_MS = 400

function extrairCampo(prompt, rotulo) {
  const m = new RegExp(`^${rotulo}:\\s*(.+)$`, 'm').exec(prompt)
  return m ? m[1].trim() : null
}

function extrairCliente(prompt) {
  const nome = extrairCampo(prompt, 'Cliente')?.split(' · ')[0]?.trim()
  return nome && nome !== 'sem conversa selecionada' ? nome : null
}

const PISTAS_RECLAMACAO = ['reclam', 'problema', 'errad', 'estrag', 'atras', 'devolv', 'reembols', 'quebrad', 'veio frio', 'faltou']
const pareceReclamacao = (texto) => PISTAS_RECLAMACAO.some((p) => texto.toLowerCase().includes(p))

// Só o trecho entre dois rótulos do prompt (ex.: a conversa em si, sem a
// pergunta da Thatiane nem o preâmbulo do sistema), para o sinal de
// reclamação não disparar por causa da própria pergunta ("já reclamou
// antes?" contém "reclam" e não é sinal nenhum vindo da conversa).
function extrairTrecho(prompt, inicioRotulo, fimRotulo) {
  const inicio = prompt.indexOf(inicioRotulo)
  if (inicio < 0) return ''
  const desde = inicio + inicioRotulo.length
  const fim = fimRotulo ? prompt.indexOf(fimRotulo, desde) : -1
  return prompt.slice(desde, fim >= 0 ? fim : undefined)
}

// Rascunho de resposta ao cliente: contrato JSON de dominio/agente.js.
function respostaFalsaRascunho(prompt) {
  const nome = extrairCliente(prompt)
  const texto = `${nome ? `Oi, ${nome}! ` : 'Oi! '}Recebemos sua mensagem e já confirmamos os detalhes com a equipe da casa. (${AVISO_SIMULADO})`
  return JSON.stringify({ acao: 'propor', texto })
}

// Pergunta livre do assistente: texto solto, usa o contexto do prompt
// (nome do cliente, pedidos anteriores, sinal de reclamação na conversa).
function respostaFalsaLivre(prompt) {
  const nome = extrairCliente(prompt)
  const pedidos = extrairCampo(prompt, 'Pedidos anteriores')
  const conversa = extrairTrecho(prompt, 'Conversa recente:', 'Pergunta da Thatiane:')
  const partes = [nome ? `Sobre ${nome}:` : 'Sobre esta conversa:']
  if (pedidos != null) partes.push(`${pedidos} pedido(s) anterior(es) por aqui.`)
  partes.push(pareceReclamacao(conversa)
    ? 'A conversa recente tem sinal de reclamação ou problema, vale confirmar com a Thatiane antes de responder sozinho.'
    : 'Não achei sinal de reclamação nas últimas mensagens.')
  partes.push(`(${AVISO_SIMULADO})`)
  return partes.join(' ')
}

function porFalso(prompt, transporteSolicitado) {
  return new Promise((resolver) => {
    const inicio = Date.now()
    setTimeout(() => {
      const ehRascunho = prompt.includes('Responda SOMENTE com um JSON')
      resolver({
        texto: ehRascunho ? respostaFalsaRascunho(prompt) : respostaFalsaLivre(prompt),
        modelo: 'falso',
        tokens: null,
        tokensSaida: null,
        custoUsd: 0,
        latenciaMs: Date.now() - inicio,
        transporte: transporteSolicitado,
      })
    }, ATRASO_FALSO_MS)
  })
}

async function servirArquivo(res, caminhoUrl) {
  const alvo = path.normalize(path.join(DIST, caminhoUrl === '/' ? 'index.html' : caminhoUrl))
  if (!alvo.startsWith(DIST) || !existsSync(alvo)) {
    res.writeHead(404, { 'content-type': 'text/plain; charset=utf-8' })
    res.end('não achei')
    return
  }
  const conteudo = await readFile(alvo)
  res.writeHead(200, { 'content-type': TIPOS[path.extname(alvo)] ?? 'application/octet-stream' })
  res.end(conteudo)
}

const TRANSPORTES = { cli: porCli, api: porApi }

const servidor = http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost')
  const responder = (codigo, corpo) => {
    res.writeHead(codigo, { 'content-type': 'application/json; charset=utf-8' })
    res.end(JSON.stringify(corpo))
  }
  try {
    if (req.method === 'GET' && url.pathname === '/api/saude') {
      responder(200, {
        ok: true,
        modo: MODO_FALSO ? 'falso' : 'real',
        cli: true,
        api: Boolean(process.env.ANTHROPIC_API_KEY || process.env.ANTHROPIC_AUTH_TOKEN),
        modeloCli: MODELO_CLI ?? 'padrão do Claude Code',
        modeloApi: MODELO_API,
      })
      return
    }
    if (req.method === 'POST' && url.pathname === '/api/agente') {
      const { prompt, transporte } = await lerCorpo(req)
      if (typeof prompt !== 'string' || !prompt.trim()) {
        responder(400, { erro: 'prompt vazio' })
        return
      }
      if (!TRANSPORTES[transporte]) {
        responder(400, { erro: 'transporte deve ser cli ou api' })
        return
      }
      const resultado = MODO_FALSO ? await porFalso(prompt, transporte) : await TRANSPORTES[transporte](prompt)
      const custo = resultado.custoUsd == null ? '' : ` US$ ${resultado.custoUsd.toFixed(4)}`
      console.log(`[${new Date().toLocaleTimeString('pt-BR')}] ${transporte} ${resultado.modelo} `
        + `${resultado.latenciaMs} ms ${resultado.tokens ?? '?'} tokens${custo}`)
      responder(200, resultado)
      return
    }
    if (req.method === 'GET') {
      if (!existsSync(DIST)) {
        responder(404, { erro: 'sem dist/. Rode npm run build, ou use npm run dev, que faz proxy de /api.' })
        return
      }
      await servirArquivo(res, decodeURIComponent(url.pathname))
      return
    }
    responder(404, { erro: 'rota desconhecida' })
  } catch (e) {
    console.error('[erro]', e.message)
    responder(500, { erro: e.message })
  }
})

servidor.listen(PORTA, HOST, () => {
  console.log(`Agente da Casa da Baba em http://${HOST}:${PORTA}`)
  if (MODO_FALSO) {
    console.log('  modo: falso, sem rede e sem processo filho (AGENTE_MODO=falso)')
  } else {
    console.log(`  cli: claude -p (${MODELO_CLI ?? 'modelo padrão do Claude Code'})`)
    console.log(`  api: ${process.env.ANTHROPIC_API_KEY || process.env.ANTHROPIC_AUTH_TOKEN
      ? MODELO_API
      : 'sem ANTHROPIC_API_KEY, modo api desligado'}`)
  }
  console.log(existsSync(DIST) ? '  tela: dist/ servida nesta porta' : '  tela: sem dist/, use npm run dev (proxy /api)')
})
