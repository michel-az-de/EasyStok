const { test } = require('node:test')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const path = require('node:path')
const vm = require('node:vm')

function carregar() {
  const config = { webhook: { url: 'http://api.test/callback', retryAttempts: 5, retryDelay: 0 }, createOptions: {} }
  let recusar
  let chamadas = 0
  const http = {
    interceptors: { response: { use: (_, rejeitar) => { recusar = rejeitar } } },
    request: async requisicao => { chamadas++; return { status: 200, config: requisicao } },
  }
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, 'configurar.cjs'), 'utf8'), {
    require: nome => {
      if (nome.endsWith('/dist/config.js')) return { default: config }
      if (nome === 'node:fs') return { readdirSync: () => [] }
      if (nome === 'node:path') return path
      if (nome === 'axios') return http
      throw new Error(`Módulo inesperado: ${nome}`)
    },
    setTimeout: concluir => concluir(),
    console: { info() {} },
  })
  const erro = (status, requisicao = { url: config.webhook.url }) =>
    Object.assign(new Error('Resposta HTTP'), { config: requisicao, response: { status } })
  return { http, recusar, erro, get chamadas() { return chamadas } }
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
