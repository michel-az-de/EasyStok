import { MODELO_PREVISTO } from '../dominio/modosDeConexao'
import { interpretarResposta } from '../dominio/agente'

// Transporte do agente. Três modos, uma forma só de resposta:
//   simulado: rascunho determinístico do domínio, nada sai da máquina.
//   cli:      servidor local (servidor/agente.mjs) roda `claude -p`.
//   api:      o mesmo servidor chama a API da Anthropic pelo SDK oficial.
// A tela não sabe qual é qual.

const ATRASO_SIMULADO_MS = 650
const ROTA = '/api/agente'
// Decisão 63: a pergunta livre nunca pode ficar presa em "Pensando" para
// sempre. Sem isto, um fetch sem timeout ficava esperando o servidor
// indefinidamente (achado P0, rodada 9). 20 s cobre a resposta saudável (5 a
// 12 s medido) com folga, e fica abaixo do teto de 25 s combinado com a
// banca. Vale para qualquer chamado por aqui: Sugerir (consultarAgente) e a
// pergunta livre do balão usam a mesma função.
const TEMPO_LIMITE_MS = 20_000

const estimarTokens = (...textos) => Math.round(textos.join('').length / 4)

function simulado({ modo, prompt, rascunho, acao, intencao, confianca }) {
  return new Promise((resolver) => {
    setTimeout(() => resolver({
      modo,
      modelo: MODELO_PREVISTO,
      intencao,
      confianca,
      acao,
      texto: rascunho,
      estruturada: true,
      prompt,
      recebidoEm: Date.now(),
      latenciaMs: ATRASO_SIMULADO_MS,
      // Estimativa por caractere, declarada como estimativa para ninguém
      // confundir com medida. O transporte real devolve a contagem medida.
      tokensEstimados: estimarTokens(prompt, rascunho),
      tokensMedidos: null,
      custoUsd: null,
      transporte: 'simulado',
    }), ATRASO_SIMULADO_MS)
  })
}

async function remoto({ modo, prompt, intencao, confianca }) {
  const inicio = Date.now()
  const controlador = new AbortController()
  const relogio = setTimeout(() => controlador.abort(), TEMPO_LIMITE_MS)
  try {
    let resposta
    try {
      resposta = await fetch(ROTA, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ prompt, transporte: modo }),
        signal: controlador.signal,
      })
    } catch (erro) {
      if (erro.name === 'AbortError') {
        throw new Error('O assistente demorou demais para responder. Tente de novo.')
      }
      throw new Error('O servidor local do agente não respondeu. Rode `npm run agente` em outro terminal.')
    }
    const corpo = await resposta.json().catch(() => ({}))
    if (!resposta.ok) throw new Error(corpo.erro ?? `O servidor respondeu ${resposta.status}.`)
    const lida = interpretarResposta(corpo.texto)
    return {
      modo,
      modelo: corpo.modelo ?? MODELO_PREVISTO,
      intencao,
      confianca,
      acao: lida.acao,
      texto: lida.texto,
      estruturada: lida.estruturada,
      prompt,
      recebidoEm: Date.now(),
      latenciaMs: corpo.latenciaMs ?? (Date.now() - inicio),
      tokensEstimados: estimarTokens(prompt, lida.texto),
      tokensMedidos: corpo.tokens ?? null,
      custoUsd: corpo.custoUsd ?? null,
      transporte: corpo.transporte ?? modo,
    }
  } finally {
    clearTimeout(relogio)
  }
}

export function pedirSugestao(pedido) {
  return pedido.modo === 'simulado' ? simulado(pedido) : remoto(pedido)
}
