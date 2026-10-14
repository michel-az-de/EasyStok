/* eslint-disable no-console */
// Prova da #1786: envio do atendimento sem mensagem dupla e laço da inbox mais leve.
//   1. o mesmo envio repetido (resposta perdida) leva a mesma Idempotency-Key; confirmado, o próximo ganha outra;
//   2. a conversa aberta só relê as mensagens quando algo pode ter mudado; aba oculta lê mais devagar.
//
//   node ferramentas/prova-1786-atendimento-envio.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  // `fonteDados.js` lê `import.meta.env` do Vite; no node a fonte é fixa.
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
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const plano = await import('../src/aplicacao/planoDeSincronizacao.js')

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

const json = (data, status = 200) => new Response(JSON.stringify(status < 300 ? { data } : data), { status })
const enviada = (id = 'srv-1') => ({ id, direcao: 'Saida', autor: 'Atendente', status: 'Enviada', enviadaEm: '2026-10-14T12:00:00Z', tipoConteudo: 'Texto', texto: 'x' })
const FOTO = 'data:image/png;base64,iVBORw0KGgo='

// Rede falsa: cada POST tira a próxima resposta da fila (`'cair'` simula a resposta perdida).
function rede(respostas) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', headers = {} } = {}) => {
    chamadas.push({ linha: `${method} ${url}`, chave: headers['Idempotency-Key'] ?? null })
    const proxima = respostas.shift() ?? json(enviada())
    if (proxima === 'cair') throw new TypeError('Failed to fetch')
    return proxima
  }
  return chamadas
}

function montar(conversas = [{ id: 'conv-1', canal: 'WhatsApp' }, { id: 'conv-2', canal: 'WhatsApp' }]) {
  const despachados = []
  const api = comApi({}, { despachar: (a) => despachados.push(a), agoraRef: { current: Date.now() }, estadoRef: { current: { conversas } } })
  return { api, despachados }
}

// --- 1. Idempotency-Key do envio ----------------------------------------------------------
await confere('1. texto: resposta perdida e o mesmo envio de novo levam a MESMA chave', async () => {
  const chamadas = rede(['cair', json(enviada())])
  const { api } = montar()
  assert.equal(await api.enviar('conv-1', 'Seu pedido saiu!'), false)
  assert.equal(await api.enviar('conv-1', 'Seu pedido saiu!'), true)
  assert.equal(chamadas.length, 2)
  assert.ok(chamadas[0].chave, 'o envio não levou Idempotency-Key')
  assert.equal(chamadas[1].chave, chamadas[0].chave)
})

await confere('1. texto: depois de confirmado, o mesmo texto é outra mensagem e ganha outra chave', async () => {
  const chamadas = rede([json(enviada('a')), json(enviada('b'))])
  const { api } = montar()
  await api.enviar('conv-1', 'ok')
  await api.enviar('conv-1', 'ok')
  assert.ok(chamadas[0].chave && chamadas[1].chave)
  assert.notEqual(chamadas[1].chave, chamadas[0].chave)
})

await confere('1. texto: outro texto ou outra conversa não reaproveitam a chave do envio perdido', async () => {
  const chamadas = rede(['cair', json(enviada('a')), json(enviada('b')), json(enviada('c'))])
  const { api } = montar()
  await api.enviar('conv-1', 'Seu pedido saiu!')
  await api.enviar('conv-2', 'Seu pedido saiu!')
  await api.enviar('conv-1', 'Seu pedido saiu!')
  await api.enviar('conv-1', 'Outro assunto')
  const [perdido, outraConversa, repetido, outroTexto] = chamadas.map((c) => c.chave)
  assert.notEqual(outraConversa, perdido)
  assert.notEqual(outroTexto, perdido)
  assert.equal(repetido, perdido)
})

await confere('1. foto: a mesma foto de novo depois da resposta perdida leva a mesma chave', async () => {
  const chamadas = rede(['cair', json(enviada())])
  const { api } = montar()
  assert.equal(await api.enviarMidia('conv-1', { formato: 'imagem', arte: FOTO, texto: '', nomeArquivo: 'bolo.png' }), false)
  assert.equal(await api.enviarMidia('conv-1', { formato: 'imagem', arte: FOTO, texto: '', nomeArquivo: 'bolo.png' }), true)
  assert.ok(chamadas[0].linha.endsWith('/mensagens/imagem'))
  assert.ok(chamadas[0].chave, 'a foto não levou Idempotency-Key')
  assert.equal(chamadas[1].chave, chamadas[0].chave)
})

await confere('1. áudio: a mesma gravação de novo leva a mesma chave', async () => {
  const chamadas = rede(['cair', json(enviada())])
  const { api } = montar()
  const audio = { formato: 'audio', arte: 'data:audio/ogg;base64,T2dnUw==', texto: '', duracaoMs: 1200 }
  await api.enviarMidia('conv-1', audio)
  await api.enviarMidia('conv-1', audio)
  assert.ok(chamadas[0].chave, 'o áudio não levou Idempotency-Key')
  assert.equal(chamadas[1].chave, chamadas[0].chave)
})

await confere('1. peça do cardápio: o mesmo item de novo leva a mesma chave', async () => {
  const chamadas = rede(['cair', json(enviada())])
  const { api } = montar()
  const peca = { formato: 'peca', arte: 'https://x/foto.jpg', texto: 'Bolo', cardapioItemId: 'item-1', indice: 0 }
  await api.enviarMidia('conv-1', peca)
  await api.enviarMidia('conv-1', peca)
  assert.ok(chamadas[0].linha.endsWith('/mensagens/imagem-cardapio'))
  assert.ok(chamadas[0].chave)
  assert.equal(chamadas[1].chave, chamadas[0].chave)
})

await confere('1. despedida do encerramento usa a mesma chave do texto', async () => {
  const chamadas = rede(['cair'])
  const { api } = montar()
  await api.enviar('conv-1', 'Obrigada, até logo!')
  const segunda = rede([json(enviada()), json({ id: 'conv-1', situacao: 'Encerrada' })])
  await api.encerrarComResumo('conv-1', Date.now(), { enviarMensagem: true, textoMensagem: 'Obrigada, até logo!' })
  assert.ok(chamadas[0].chave, 'o envio não levou Idempotency-Key')
  assert.equal(segunda[0].chave, chamadas[0].chave)
})

await confere('1. a API diz que a chave já saiu com outro corpo: o balão não diz "não enviada"', async () => {
  rede([json({ error: { code: 'IDEMPOTENCY_PAYLOAD_MISMATCH', message: 'A chave de idempotencia ja foi usada com um corpo diferente.' } }, 422)])
  const { api, despachados } = montar()
  await api.enviarMidia('conv-1', { formato: 'imagem', arte: FOTO, texto: '' })
  const falha = despachados.find((a) => a.tipo === acao.FALHAR_ENVIO_API)
  assert.ok(falha, 'sem balão de falha')
  assert.doesNotMatch(falha.erro, /Não enviada|idempotencia/i)
  assert.match(falha.erro, /já tinha saído/)
})

await confere('1. o mesmo envio ainda em andamento na API: o balão não diz "não enviada"', async () => {
  rede([json({ error: { code: 'IDEMPOTENCY_IN_FLIGHT', message: 'Outra operacao com esta chave ja esta em andamento.' } }, 409)])
  const { api, despachados } = montar()
  await api.enviar('conv-1', 'oi')
  const falha = despachados.find((a) => a.tipo === acao.FALHAR_ENVIO_API)
  assert.doesNotMatch(falha.erro, /Não enviada|operacao/i)
  assert.match(falha.erro, /ainda está saindo/i)
})

// --- 2. Laço da inbox ----------------------------------------------------------------------
const resumo = (extra = {}) => ({ id: 'conv-1', situacao: 'Assumida', ultimaMensagemEm: '2026-10-14T12:00:00', ...extra })
const msg = (extra = {}) => ({ id: 'm', direcao: 'Saida', autor: 'Atendente', tipoConteudo: 'Texto', status: 'Lida', externoId: 'wamid.1', ...extra })
const guardado = (mensagens = [msg()]) => ({ ultima: '2026-10-14T12:00:00', mensagens })

await confere('2. conversa aberta sem mudança e sem nada em trânsito fica no cache', () => {
  assert.equal(plano.deveRelerMensagens(resumo(), guardado(), 'conv-1'), false)
  assert.equal(plano.deveRelerMensagens(resumo(), guardado([msg({ status: 'Entregue' }), msg({ direcao: 'Entrada', autor: 'Cliente', status: 'Recebida' })]), 'conv-1'), false)
})

await confere('2. conversa aberta relê com mensagem nova, sem cache ou no ciclo lento', () => {
  assert.equal(plano.deveRelerMensagens(resumo({ ultimaMensagemEm: '2026-10-14T12:01:00' }), guardado(), 'conv-1'), true)
  assert.equal(plano.deveRelerMensagens(resumo(), undefined, 'conv-1'), true)
  assert.equal(plano.deveRelerMensagens(resumo(), guardado(), 'conv-1', { cicloLento: true }), true)
})

await confere('2. conversa aberta relê enquanto o status de uma saída ainda pode mudar (enviando, enviada, falhou)', () => {
  for (const status of ['Pendente', 'Enviada', 'Falhou']) {
    assert.equal(plano.deveRelerMensagens(resumo(), guardado([msg({ status })]), 'conv-1'), true, status)
  }
})

await confere('2. nota interna (sistema, sem id do canal) "Pendente" para sempre não prende a releitura', () => {
  assert.equal(plano.deveRelerMensagens(resumo(), guardado([msg({ autor: 'Sistema', status: 'Pendente', externoId: null })]), 'conv-1'), false)
})

await confere('2. foto ou áudio do cliente ainda baixando relê; baixado ou que falhou fica no cache', () => {
  const entrada = (extra) => msg({ direcao: 'Entrada', autor: 'Cliente', tipoConteudo: 'Imagem', status: 'Recebida', ...extra })
  assert.equal(plano.deveRelerMensagens(resumo(), guardado([entrada({ midiaChave: null })]), 'conv-1'), true)
  assert.equal(plano.deveRelerMensagens(resumo(), guardado([entrada({ midiaChave: 'k/1.jpg' })]), 'conv-1'), false)
  assert.equal(plano.deveRelerMensagens(resumo(), guardado([entrada({ midiaChave: null, midiaFalhou: true })]), 'conv-1'), false)
})

await confere('2. conversas não abertas seguem como antes: só quando mudaram', () => {
  assert.equal(plano.deveRelerMensagens(resumo(), guardado([msg({ status: 'Enviada' })]), 'outra'), false)
  assert.equal(plano.deveRelerMensagens(resumo({ ultimaMensagemEm: '2026-10-14T12:01:00' }), guardado(), 'outra'), true)
})

await confere('2. visibilidade da aba; sem documento (node, provas) a aba conta como visível', () => {
  assert.equal(plano.abaVisivel({ visibilityState: 'hidden' }), false)
  assert.equal(plano.abaVisivel({ visibilityState: 'visible' }), true)
  assert.equal(plano.abaVisivel(undefined), true)
})

await confere('2. aba que ficou oculta 1 min ou mais varre a inbox inteira ao voltar; um instante, não', () => {
  const agora = Date.parse('2026-10-14T12:00:00Z')
  assert.equal(plano.deveVarrerAoVoltar(agora - 60000, agora), true)
  assert.equal(plano.deveVarrerAoVoltar(agora - 5000, agora), false)
  assert.equal(plano.deveVarrerAoVoltar(null, agora), false)
})

await confere('2. com a aba oculta o laço segue mais devagar (o aviso de mensagem nova depende dele) e volta ao normal quando ela aparece', () => {
  const fonte = readFileSync(new URL('../src/aplicacao/useSincronizacaoApi.js', import.meta.url), 'utf8')
  assert.match(fonte, /setTimeout\(ciclo, abaVisivel\(\) \? INTERVALO_MS : INTERVALO_OCULTA_MS\)/)
  assert.match(fonte, /addEventListener\('visibilitychange'/)
  assert.match(fonte, /removeEventListener\('visibilitychange'/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
