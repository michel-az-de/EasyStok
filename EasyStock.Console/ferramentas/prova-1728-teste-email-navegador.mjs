// Sessão e HTTP controlados. Nunca dispara e-mail real nem lê token da sessão do usuário.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5749'
const evidencias = process.env.EVIDENCIAS_UX ?? '.build/prova-1728-email'
mkdirSync(evidencias, { recursive: true })
const empresaId = '11111111-1111-4111-8111-111111111111'
const eventoId = '33333333-3333-4333-8333-333333333333'
const casos = [
  { nome: 'pendente-enviado', nivel: 'SuperAdmin', status: 'Enviado', texto: 'O provedor aceitou o envio.' },
  { nome: 'evento-pendente-sem-mensagem', nivel: 'SuperAdmin', status: 'Enviado', pendenteInicial: true, texto: 'O provedor aceitou o envio.' },
  { nome: 'celular-enviado', nivel: 'SuperAdmin', status: 'Enviado', celular: true, texto: 'O provedor aceitou o envio.' },
  { nome: 'simulado', nivel: 'SuperAdmin', status: 'Simulado', texto: 'Nenhum e-mail foi enviado.' },
  { nome: 'falhado', nivel: 'SuperAdmin', status: 'Falhado', texto: 'O envio falhou.' },
  { nome: 'erro-consulta-retomada', nivel: 'SuperAdmin', status: 'Enviado', erroConsulta: true, texto: 'O provedor aceitou o envio.' },
  { nome: 'email-nao-confirmado', nivel: 'SuperAdmin', rejeitado: true },
  { nome: 'admin-sem-ferramenta', nivel: 'Admin' },
  { nome: 'perfil-desconhecido-sem-ferramenta' },
]
const browser = await chromium.launch({ headless: true })
try {
  for (const caso of casos) {
    const context = await browser.newContext({ viewport: caso.celular ? { width: 390, height: 844 } : { width: 1440, height: 960 }, reducedMotion: 'reduce' })
    await context.grantPermissions(['local-network-access'], { origin: origem })
    await context.addInitScript(({ empresaId, nivel }) => sessionStorage.setItem('easystok.sessao', JSON.stringify({
      token: 'teste-ui-controlado', expiraEm: Date.now() + 3600000, empresa: { id: empresaId, nome: 'Empresa de teste' },
      usuario: { id: '22222222-2222-4222-8222-222222222222', nome: 'Felipe de teste', email: 'felipe@exemplo.invalid', nivel },
    })), { empresaId, nivel: caso.nivel })
    const posts = []
    let consultas = 0
    let liberarConsulta
    const esperaConsulta = new Promise(resolve => { liberarConsulta = resolve })
    await context.route('**/api/**', async route => {
      const caminho = new URL(route.request().url()).pathname
      if (!caminho.startsWith('/api/')) return route.continue()
      const ok = data => route.fulfill({ json: { data } })
      if (caminho === '/api/auth/me/modulos') return ok({ modulos: ['cardapio', 'producao', 'atendimento', 'cozinha', 'financeiro', 'campanhas', 'configuracoes', 'entregas'].map(id => ({ id, liberado: true })), acoes: {} })
      if (caminho === '/api/integracoes/whatsapp/status') return ok({ phoneNumberId: 'numero-controlado', provider: 'meta' })
      if (caminho === '/api/integracoes/whatsapp/coexistencia/config') return ok({ habilitado: false })
      if (caminho === '/api/admin/notificacoes/disparo-teste') {
        posts.push(route.request().postDataJSON())
        if (caso.rejeitado) return route.fulfill({ status: 400, json: { error: { code: 'EMAIL_NAO_CONFIRMADO', message: 'O e-mail da sua conta precisa estar confirmado.' } } })
        return route.fulfill({ status: 202, json: { data: { eventoId, tipo: 'ContatoAlterado' } } })
      }
      if (caminho === `/api/admin/notificacoes/disparo-teste/${eventoId}`) {
        consultas++
        await esperaConsulta
        if (caso.pendenteInicial && consultas === 1) return ok({ eventoId, tipo: 'ContatoAlterado', eventoStatus: 'Pendente', eventoErro: null, mensagens: [] })
        if (caso.erroConsulta && consultas === 1) return route.fulfill({ status: 500, json: { error: { message: 'Consulta indisponível na prova.' } } })
        return ok({ eventoId, tipo: 'ContatoAlterado', eventoStatus: 'Processado', eventoErro: null,
          mensagens: [{ canal: 'Email', status: caso.status, provider: caso.status === 'Simulado' ? 'console' : 'smtp', tentativas: 1, erro: caso.status === 'Falhado' ? 'Falha controlada.' : null }] })
      }
      if (caminho === '/api/notificacoes/badge') return ok({ count: 0 })
      return ok([])
    })
    const page = await context.newPage()
    page.setDefaultTimeout(10000)
    const erros = []
    page.on('pageerror', erro => erros.push(erro.message))
    try {
      await page.goto(origem + '/#/m/configuracoes/canais', { waitUntil: 'domcontentloaded' })
      await page.getByRole('heading', { name: 'WhatsApp Business', exact: true }).waitFor()
      if (caso.nivel !== 'SuperAdmin') {
        assert.equal(await page.getByText('Homologação: testar meu e-mail', { exact: true }).count(), 0)
        assert.equal(posts.length, 0)
        assert.equal(consultas, 0)
      } else {
        await page.getByText('Homologação: testar meu e-mail', { exact: true }).click()
        await page.getByText(/e-mail confirmado da sua própria conta: felipe@exemplo.invalid/).waitFor()
        const enviar = page.getByRole('button', { name: 'Enviar teste para meu e-mail', exact: true })
        assert.equal(await enviar.isDisabled(), true)
        await page.getByLabel('Motivo do teste', { exact: true }).fill('Homologação de e-mail próprio autorizada por Felipe.')
        await enviar.click()
        if (caso.rejeitado) {
          await page.getByRole('alert').filter({ hasText: 'precisa estar confirmado' }).waitFor()
          assert.equal(consultas, 0)
        } else {
          await page.getByText('Teste solicitado. Aguardando processamento.', { exact: true }).waitFor()
          assert.equal(await enviar.isDisabled(), true)
          await page.screenshot({ path: join(evidencias, caso.nome + '-202.png'), fullPage: true })
          liberarConsulta()
          if (caso.pendenteInicial) {
            await page.getByText('Teste solicitado. Aguardando processamento.', { exact: true }).waitFor()
            assert.equal(await enviar.isDisabled(), true)
          }
          if (caso.erroConsulta) {
            await page.getByRole('alert').filter({ hasText: 'Não foi possível consultar o resultado.' }).waitFor()
            await page.getByRole('button', { name: 'Consultar resultado novamente', exact: true }).click()
          }
          await page.getByText(caso.texto, { exact: false }).waitFor()
          await page.getByText(caso.texto, { exact: false }).scrollIntoViewIfNeeded()
          assert.equal(await page.getByText(/e-mail foi entregue/).count(), 0)
        }
        assert.deepEqual(posts, [{ tipo: 'ContatoAlterado', motivo: 'Homologação de e-mail próprio autorizada por Felipe.' }])
        assert.equal(await page.locator('input[type="email"]').count(), 0)
      }
      assert.deepEqual(erros, [])
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true)
      await page.evaluate(() => document.fonts.ready)
      await page.screenshot({ path: join(evidencias, caso.nome + '.png'), fullPage: true })
      console.log('ok ' + caso.nome)
    } finally {
      liberarConsulta()
      await context.close()
    }
  }
} finally { await browser.close() }
console.log(`OK: ${casos.length} cenários de homologação do e-mail próprio; HTTP controlado, sem envio real.`)
