/* eslint-disable no-console */
// Prova da issue #1444: no modo API o áudio gravado sai pelo WhatsApp. O balão nasce "enviando",
// o console espera a resposta do EasyStok e mostra o erro quando não sai. O gravador prefere o
// formato que a Meta aceita direto (Ogg no Firefox) e cai no WebM do Chrome, que o backend converte.
//
//   node ferramentas/prova-1444-audio-whatsapp.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    if (url.endsWith('.json')) {
      return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    }
    return proximo(url, contexto)
  },
})

const acao = await import('../src/aplicacao/acoes.js')
const { criarAcoesEncerramentoEMidiaApi } = await import('../src/aplicacao/api/encerramentoEMidia.js')
const { mensagemDeAudio } = await import('../src/dominio/anexos.js')
const { escolherFormatoGravacao, nomeDoArquivoDeAudio } = await import('../src/dominio/audio.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

// ── formato da gravação ─────────────────────────────────────────────
const suporta = (...tipos) => (tipo) => tipos.includes(tipo)

await confere('Firefox grava Ogg/Opus, que a Meta aceita direto', () => {
  assert.equal(escolherFormatoGravacao(suporta('audio/ogg;codecs=opus', 'audio/webm;codecs=opus')), 'audio/ogg;codecs=opus')
})

await confere('Chrome grava WebM/Opus (o EasyStok converte), não Opus dentro de MP4', () => {
  assert.equal(escolherFormatoGravacao(suporta('audio/webm;codecs=opus', 'audio/mp4', 'audio/webm')), 'audio/webm;codecs=opus')
})

await confere('Safari grava MP4', () => {
  assert.equal(escolherFormatoGravacao(suporta('audio/mp4')), 'audio/mp4')
})

await confere('navegador sem nenhum dos três usa o padrão dele', () => {
  assert.equal(escolherFormatoGravacao(suporta()), null)
  assert.equal(escolherFormatoGravacao(undefined), null)
})

await confere('nome do arquivo segue o tipo gravado', () => {
  assert.equal(nomeDoArquivoDeAudio('audio/webm;codecs=opus'), 'audio.webm')
  assert.equal(nomeDoArquivoDeAudio('audio/ogg'), 'audio.ogg')
  assert.equal(nomeDoArquivoDeAudio('audio/mp4'), 'audio.m4a')
  assert.equal(nomeDoArquivoDeAudio(''), 'audio')
})

// ── envio ───────────────────────────────────────────────────────────
const CONVERSA = 'conv-audio'
const GRAVADO = mensagemDeAudio({ dataUrl: 'data:audio/webm;codecs=opus;base64,GkXfo59ChoEB', duracaoMs: 4200 })
const RESPOSTA = {
  id: 'm-servidor', direcao: 'Saida', autor: 'Dona', tipoConteudo: 'Audio', texto: null,
  midiaChave: 'atendimento/x/voz.ogg', midiaMime: 'audio/ogg', status: 'Enviada', enviadaEm: '2026-10-07T15:00:00',
}

function montar({ canal = 'WhatsApp', responder }) {
  const linha = []
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    const arquivo = body instanceof FormData ? body.get('file') : null
    linha.push({ evento: 'fetch', metodo: method, url, arquivo })
    return responder()
  }
  const estadoRef = { current: { conversas: [{ id: CONVERSA, canal }] } }
  const acoes = criarAcoesEncerramentoEMidiaApi({
    despachar: (a) => linha.push({ evento: 'despacho', ...a }),
    agoraRef: { current: '2026-10-07T15:00:00Z' },
    estadoRef,
    falhaDoEnvio: (id, mensagemId, erro) => ({
      tipo: acao.FALHAR_ENVIO_API, id, mensagemId, erro: erro.message,
      ...(erro.dados?.id ? { mensagem: { id: erro.dados.id } } : { semIdServidor: true }),
    }),
  })
  return { acoes, linha }
}

const resposta = (status, corpo) => new Response(JSON.stringify(corpo), { status })

await confere('áudio no WhatsApp: balão "enviando" antes do envio, multipart na rota de áudio, espera o resultado', async () => {
  const { acoes, linha } = montar({ responder: () => resposta(200, { data: RESPOSTA }) })
  const saiu = await acoes.enviarMidia(CONVERSA, GRAVADO)

  assert.equal(saiu, true)
  const iEnvio = linha.findIndex((e) => e.tipo === acao.ENVIAR_MIDIA)
  const iFetch = linha.findIndex((e) => e.evento === 'fetch')
  assert.ok(iEnvio >= 0 && iEnvio < iFetch, 'o balão precisa aparecer antes de o envio terminar')
  const balao = linha[iEnvio]
  assert.equal(balao.formato, 'audio')
  assert.equal(balao.status, 'enviando')
  assert.equal(balao.duracaoMs, 4200)

  const envio = linha[iFetch]
  assert.equal(envio.metodo, 'POST')
  assert.match(envio.url, /\/api\/atendimento\/conversas\/conv-audio\/mensagens\/audio$/)
  assert.ok(envio.arquivo, 'sem arquivo no multipart')
  assert.equal(envio.arquivo.name, 'audio.webm')
  assert.equal(envio.arquivo.type, 'audio/webm')

  const confirmado = linha.find((e) => e.tipo === acao.CONFIRMAR_ENVIO_API)
  assert.ok(confirmado, 'não confirmou o envio')
  assert.equal(confirmado.mensagemId, balao.mensagemId)
  assert.equal(confirmado.mensagem.id, 'm-servidor')
  assert.equal(confirmado.mensagem.status, 'enviada')
  // O balão confirmado continua tocando o que ela gravou (o arquivo do servidor chega na sincronização).
  assert.equal(confirmado.mensagem.formato, 'audio')
  assert.equal(confirmado.mensagem.arte, GRAVADO.arte)
  assert.equal(confirmado.mensagem.duracaoMs, 4200)
  assert.equal(linha.some((e) => e.tipo === acao.AVISO_API), false, 'não pode avisar "não ligado"')
})

await confere('falha na Meta: balão vira falhou com o motivo e a ação devolve false', async () => {
  const { acoes, linha } = montar({
    responder: () => resposta(502, { error: { code: 'CANAL_FALHOU', message: 'Falha ao enviar pelo canal.', details: { id: 'm-falhou' } } }),
  })
  const saiu = await acoes.enviarMidia(CONVERSA, GRAVADO)

  assert.equal(saiu, false)
  const falha = linha.find((e) => e.tipo === acao.FALHAR_ENVIO_API)
  assert.ok(falha, 'não marcou a falha')
  assert.equal(falha.erro, 'Falha ao enviar pelo canal.')
  assert.equal(falha.mensagem?.id, 'm-falhou')
})

await confere('formato recusado (400): balão falhou com o motivo do EasyStok', async () => {
  const { acoes, linha } = montar({
    responder: () => resposta(400, { error: { code: 'VALIDATION', message: 'Esse formato de áudio não sai pelo WhatsApp.' } }),
  })
  assert.equal(await acoes.enviarMidia(CONVERSA, GRAVADO), false)
  const falha = linha.find((e) => e.tipo === acao.FALHAR_ENVIO_API)
  assert.match(falha.erro, /formato de áudio/)
  assert.equal(falha.semIdServidor, true)
})

await confere('canal sem áudio no EasyStok (Instagram): avisa e não chama a API', async () => {
  const { acoes, linha } = montar({ canal: 'Instagram', responder: () => resposta(200, { data: RESPOSTA }) })
  assert.equal(await acoes.enviarMidia(CONVERSA, GRAVADO), false)
  assert.equal(linha.some((e) => e.evento === 'fetch'), false)
  const aviso = linha.find((e) => e.tipo === acao.AVISO_API)
  assert.match(aviso.mensagem, /Áudio/)
  assert.match(aviso.mensagem, /WhatsApp/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
