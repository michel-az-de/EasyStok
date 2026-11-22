const { test } = require('node:test')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const path = require('node:path')
const vm = require('node:vm')
const crypto = require('node:crypto')
const SEGREDO = 'segredo-webhook-2071'
const { Buffer } = require('node:buffer')

function carregar(env = { WPP_WEBHOOK_TOKEN: SEGREDO }) {
  const config = { webhook: { url: 'http://api.test/callback', retryAttempts: 5, retryDelay: 0 }, createOptions: {} }
  let recusar
  let assinar
  let chamadas = 0
  const http = {
    interceptors: {
      request: { use: preparar => { assinar = preparar } },
      response: { use: (_, rejeitar) => { recusar = rejeitar } },
    },
    request: async requisicao => { chamadas++; return { status: 200, config: requisicao } },
  }
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, 'configurar.cjs'), 'utf8'), {
    require: nome => {
      if (nome.endsWith('/dist/config.js')) return { default: config }
      if (nome === 'node:fs') return { readdirSync: () => [] }
      if (nome === 'node:path') return path
      if (nome === 'node:crypto') return crypto
      if (nome === 'axios') return http
      throw new Error(`Módulo inesperado: ${nome}`)
    },
    setTimeout: concluir => concluir(),
    console: { info() {} },
    process: { env },
  })
  const erro = (status, requisicao = { url: config.webhook.url }) =>
    Object.assign(new Error('Resposta HTTP'), { config: requisicao, response: { status } })
  return { http, recusar, erro, config, get assinar() { return assinar }, get chamadas() { return chamadas } }
}

for (const status of [429, 500, 503]) {
  test(`callback HTTP ${status} repete e preserva a requisição`, async () => {
    const ambiente = carregar()
    const erro = ambiente.erro(status)
    const resposta = await ambiente.recusar(erro)
    assert.equal(resposta.status, 200)
    assert.equal(resposta.config, erro.config)
    assert.equal(ambiente.chamadas, 1)
  })
}

test('recusa permanente e endpoint de envio não são repetidos', async () => {
  const ambiente = carregar()
  for (const erro of [ambiente.erro(400), ambiente.erro(401), ambiente.erro(503, { url: 'http://wpp.test/send-message' })]) {
    await assert.rejects(ambiente.recusar(erro), e => e === erro)
  }
  assert.equal(ambiente.chamadas, 0)
})

test('callback sempre indisponível para após cinco tentativas', async () => {
  const ambiente = carregar()
  let repeticoes = 0
  ambiente.http.request = requisicao => {
    repeticoes++
    return ambiente.recusar(ambiente.erro(503, requisicao))
  }
  await assert.rejects(ambiente.recusar(ambiente.erro(503)))
  assert.equal(repeticoes, 4)
})

const hmac = corpo => crypto.createHmac('sha512', SEGREDO).update(Buffer.from(corpo, 'utf8')).digest('hex')

test('callback leva o corpo assinado em X-Webhook-Hmac e nenhum segredo na URL', () => {
  const ambiente = carregar()
  const corpo = { event: 'onmessage', session: 'ez-1', body: 'olá, pão 🍞' }
  const requisicao = ambiente.assinar({ url: ambiente.config.webhook.url, method: 'post', data: corpo, headers: {} })
  assert.equal(typeof requisicao.data, 'string')
  assert.deepEqual(JSON.parse(requisicao.data), corpo)
  assert.equal(requisicao.headers['Content-Type'], 'application/json')
  assert.equal(requisicao.headers['X-Webhook-Hmac'], hmac(requisicao.data))
  // O axios não transforma de novo: o byte assinado é o byte enviado.
  assert.equal(requisicao.transformRequest.reduce((d, f) => f(d, requisicao.headers), requisicao.data), requisicao.data)
  assert.ok(!requisicao.url.includes(SEGREDO))
})

test('repetição reassina o mesmo corpo sem serializar de novo', () => {
  const ambiente = carregar()
  const primeira = ambiente.assinar({ url: ambiente.config.webhook.url, data: { a: 1 }, headers: {} })
  const corpo = primeira.data
  const segunda = ambiente.assinar(primeira)
  assert.equal(segunda.data, corpo)
  assert.equal(segunda.headers['X-Webhook-Hmac'], hmac(corpo))
})

test('chamada que não é o callback não recebe assinatura', () => {
  const ambiente = carregar()
  const requisicao = ambiente.assinar({ url: 'http://wpp.test/send-message', data: { a: 1 }, headers: {} })
  assert.deepEqual(requisicao.data, { a: 1 })
  assert.equal(requisicao.headers['X-Webhook-Hmac'], undefined)
})

test('sem WPP_WEBHOOK_TOKEN, ou com ele curto, o servidor não sobe', () => {
  assert.throws(() => carregar({}), /WPP_WEBHOOK_TOKEN/)
  assert.throws(() => carregar({ WPP_WEBHOOK_TOKEN: 'curto' }), /WPP_WEBHOOK_TOKEN/)
})
