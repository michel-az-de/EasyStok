// HTTP controlado: estado do canal e disponibilidade do cadastro pela Meta são fatos distintos.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5748'
const evidencias = process.env.EVIDENCIAS_UX ?? '.build/prova-1722-canais'
mkdirSync(evidencias, { recursive: true })
const empresaId = '11111111-1111-4111-8111-111111111111'
const ligado = { phoneNumberId: 'numero-controlado', provider: 'meta', ultimaMensagemRecebidaEm: '2026-10-13T12:00:00Z' }
const casos = [
  { nome: 'ligado-cadastro-indisponivel', status: ligado, ligado: true },
  { nome: 'sem-numero-cadastro-indisponivel', status: { phoneNumberId: null, provider: 'meta' }, ligado: false },
  { nome: 'status-pendente-cadastro-indisponivel', pendente: true, ligado: false },
  { nome: 'status-erro-cadastro-indisponivel', erro: true, ligado: false },
]
const browser = await chromium.launch({ headless: true })
const falhas = []
try {
  for (const caso of casos) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 960 }, reducedMotion: 'reduce' })
    await context.grantPermissions(['local-network-access'], { origin: origem })
    await context.addInitScript(({ empresaId }) => sessionStorage.setItem('easystok.sessao', JSON.stringify({
      token: 'teste-ui-controlado', expiraEm: Date.now() + 3600000,
      empresa: { id: empresaId, nome: 'Empresa de teste' }, usuario: { id: '22222222-2222-4222-8222-222222222222', nome: 'Dona de teste' },
    })), { empresaId })
    let liberarStatus
    const esperaStatus = new Promise(resolve => { liberarStatus = resolve })
    await context.route('**/api/**', async route => {
      const caminho = new URL(route.request().url()).pathname
      if (!caminho.startsWith('/api/')) return route.continue()
      const ok = data => route.fulfill({ json: { data } })
      if (caminho === '/api/auth/me/modulos') return ok({
        modulos: ['cardapio', 'producao', 'atendimento', 'cozinha', 'financeiro', 'campanhas', 'configuracoes', 'entregas'].map(id => ({ id, liberado: true })), acoes: {},
      })
      if (caminho === '/api/integracoes/whatsapp/coexistencia/config') return ok({ habilitado: false })
      if (caminho === '/api/integracoes/whatsapp/status') {
        if (caso.pendente) await esperaStatus
        if (caso.erro) return route.fulfill({ status: 500, json: { error: { message: 'Falha controlada ao consultar o canal.' } } })
        return ok(caso.status ?? null)
      }
      if (caminho === '/api/notificacoes/badge') return ok({ count: 0 })
      return ok([])
    })
    const page = await context.newPage()
    page.setDefaultTimeout(8000)
    try {
      await page.goto(origem + '/#/m/configuracoes/canais', { waitUntil: 'domcontentloaded' })
      await page.getByRole('heading', { name: 'WhatsApp Business', exact: true }).waitFor()
      await page.getByRole('note').waitFor()
      if (caso.ligado) await page.getByText('WhatsApp ligado', { exact: true }).waitFor()
      else if (caso.erro) await page.getByRole('alert').filter({ hasText: 'Falha controlada' }).waitFor()
      else if (!caso.pendente) await page.getByText('Nenhum número vinculado à loja', { exact: true }).waitFor()
      await page.evaluate(() => document.fonts.ready)
      await page.screenshot({ path: join(evidencias, caso.nome + '.png'), fullPage: true })
      assert.equal(await page.getByRole('button', { name: 'Conectar WhatsApp Business', exact: true }).count(), 0,
        'não oferece botão desabilitado quando cadastro pela Meta está indisponível')
      assert.equal(await page.getByText('WhatsApp ligado', { exact: true }).count(), caso.ligado ? 1 : 0)
      if (caso.ligado) {
        assert.equal(await page.getByText(/O canal segue ligado/).count(), 1)
        assert.equal(await page.getByText(/A conexão do WhatsApp ainda não está habilitada/).count(), 0)
      }
      console.log('ok ' + caso.nome)
    } catch (erro) {
      await page.screenshot({ path: join(evidencias, caso.nome + '-falha.png'), fullPage: true })
      falhas.push(caso.nome + ': ' + erro.message)
    } finally {
      liberarStatus()
      await context.close()
    }
  }
} finally {
  await browser.close()
}
assert.deepEqual(falhas, [], falhas.join('\n'))
console.log(`OK: ${casos.length} estados de Canais, com capturas; HTTP controlado, sem conectar provedores.`)
