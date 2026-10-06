import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import { verificarHttp } from './verificar-http.mjs'

const sha = 'a'.repeat(40)
async function cenario(t, alterar = () => null) {
  const servidor = createServer((req, res) => {
    if (alterar(req, res)) return
    res.end(req.url === '/health/version' ? JSON.stringify({ buildSha: sha })
      : req.url === '/' ? `<html><meta name="easystok-console-sha" content="${sha}"></html>` : 'Healthy')
  })
  await new Promise(resolve => servidor.listen(0, '127.0.0.1', resolve))
  t.after(() => { servidor.closeAllConnections(); servidor.close() })
  const url = `http://127.0.0.1:${servidor.address().port}`
  return { api: url, console: url, apiSha: sha, consoleSha: sha, timeoutMs: 1000 }
}
test('aprova somente API pronta e as duas versões esperadas', async t => {
  assert.equal((await verificarHttp(await cenario(t))).pronta, true)
})
for (const rota of ['/health/ready', '/health/version', '/']) {
  test(`HTTP 404 em ${rota} reprova`, async t => {
    const config = await cenario(t, (req, res) => { if (req.url !== rota) return false; res.writeHead(404); res.end(); return true })
    await assert.rejects(verificarHttp(config), /HTTP 404/)
  })
}
test('API saudável com versão antiga reprova', async t => {
  await assert.rejects(verificarHttp({ ...await cenario(t), apiSha: 'b'.repeat(40) }), /API com versão diferente/)
})
test('console antigo ou sem marcador reprova', async t => {
  const config = await cenario(t, (req, res) => { if (req.url !== '/') return false; res.end('<html>Login</html>'); return true })
  await assert.rejects(verificarHttp(config), /Console com versão diferente ou ausente/)
})
test('timeout reprova', async t => {
  const config = await cenario(t, () => true)
  await assert.rejects(verificarHttp({ ...config, timeoutMs: 30 }), { name: 'TimeoutError' })
})
test('versão esperada é obrigatória', async t => {
  await assert.rejects(verificarHttp({ ...await cenario(t), apiSha: undefined }), /SHAs completos/)
})
