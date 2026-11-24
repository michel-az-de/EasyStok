// #3241 (fatia F da spec 3237): Cadastros > Clientes mostra a foto de cada cliente na linha, ou as iniciais
// quando não há foto, e só baixa a foto da linha que chegou perto da área visível. Dados fictícios, 1920 e 1440; 390 não quebra.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3241')
mkdirSync(pasta, { recursive: true })
const URL_CONSOLE = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64')
const idDe = (n) => `a${String(n).padStart(7, '0')}-1111-4111-8111-111111111111`
// 60 clientes fictícios; os 3 primeiros têm foto, o 4º não, e o 60º (bem abaixo da dobra) tem foto.
const comFoto = new Set([1, 2, 3, 60])
const clientes = Array.from({ length: 60 }, (_, i) => {
  const n = i + 1
  return { id: idDe(n), revisao: 1, nome: `Cliente Ficticio ${String(n).padStart(2, '0')}`, telefone: `1191111${String(1000 + n)}`, email: '', documento: '', tipoPessoa: 'fisica', ativo: true, orderCount: 0, fotoUrl: comFoto.has(n) ? `/api/clientes/${idDe(n)}/foto?v=${'b'.repeat(32)}` : null }
})
const browser = await chromium.launch({ headless: true })
const erros = []

// Cada largura roda em paralelo (contexto próprio), então cada uma guarda as próprias fotos baixadas.
async function preparar(largura) {
  const fotosBaixadas = []
  const movel = largura < 700
  const contexto = await browser.newContext({ viewport: { width: largura, height: movel ? 844 : 1080 }, isMobile: movel, hasTouch: movel, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja' }, usuario: { id: 'dona', nome: 'Dona de teste' } })))
  const acoes = Object.fromEntries(['verPedidos', 'atenderConversas', 'editarAtendimento', 'verCadastrosClientes', 'verCadastrosProdutos', 'verCadastrosFornecedores', 'verCadastrosFinanceiros'].map((n) => [n, true]))
  await contexto.route('**/api/**', async (route) => {
    const req = route.request(), path = new URL(req.url()).pathname
    const ok = (data, extra = {}) => route.fulfill({ json: { data, ...extra } })
    if (!path.startsWith('/api/')) return route.continue()
    if (/^\/api\/clientes\/[^/]+\/foto$/.test(path)) { fotosBaixadas.push(path.split('/')[3]); return route.fulfill({ status: 200, contentType: 'image/png', body: PNG }) }
    if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros', 'cardapio', 'financeiro'].map((id) => ({ id, liberado: true })), acoes })
    if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
    if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
    if (path === '/api/notificacoes/badge') return ok({ count: 0 })
    if (path === '/api/clientes') return ok(clientes, { meta: { total: clientes.length, totalCount: clientes.length, page: 1, limit: 200 } })
    return ok([], { meta: { totalCount: 0, total: 0, page: 1, limit: 20 } })
  })
  const page = await contexto.newPage()
  page.setDefaultTimeout(12000)
  page.on('pageerror', (e) => erros.push(e.message))
  await page.goto(`${URL_CONSOLE}/#/m/cadastros/clientes`)
  await page.getByText('Cliente Ficticio 01', { exact: true }).waitFor()
  return { contexto, page, fotosBaixadas }
}
const linha = (page, n) => page.locator('tbody tr').filter({ hasText: `Cliente Ficticio ${String(n).padStart(2, '0')}` })
const falhas = []
const item = async (nome, corpo) => {
  try { await corpo(); console.log(`ok    ${nome}`) } catch (e) { const motivo = e.message.split('\n').slice(0, 4).join(' | '); falhas.push(`${nome}: ${motivo}`); console.log(`FALHA ${nome}: ${motivo}`) }
}

await Promise.all([1920, 1440, 390].map((largura) =>
  item(`${largura}: foto na linha com fotoUrl, iniciais na sem foto, carga só da linha visível`, async () => {
    const { contexto, page, fotosBaixadas } = await preparar(largura)
    const foto = linha(page, 1).locator('img[alt="Foto de Cliente Ficticio 01"]')
    await foto.waitFor()
    await page.waitForFunction(() => { const i = document.querySelector('tbody tr img'); return i && i.complete && i.naturalWidth > 0 })
    assert.match(await foto.getAttribute('src'), /^data:image\/png/, 'a foto sai do download autenticado, em data URL')
    assert.equal(await linha(page, 4).locator('img').count(), 0, 'sem foto, sem imagem')
    assert.equal((await linha(page, 4).locator('[aria-hidden="true"]').filter({ hasText: 'CF' }).count()), 1, 'sem foto, iniciais CF')
    assert.deepEqual([...new Set(fotosBaixadas)].sort(), [idDe(1), idDe(2), idDe(3)], 'só as linhas visíveis baixam a foto')
    const caixa = await foto.boundingBox()
    assert.ok(caixa.width >= 32 && Math.abs(caixa.width - caixa.height) < 1, 'avatar redondo de 32 px ou mais')
    const rolagem = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
    assert.ok(rolagem <= 0, `sem rolagem horizontal (${rolagem}px)`)
    await page.screenshot({ path: `${pasta}/grid-${largura}.png` })
    // Rolar até a linha 60 baixa a foto dela, e só então.
    assert.ok(!fotosBaixadas.includes(idDe(60)), 'a linha 60 não baixou antes de aparecer')
    await linha(page, 60).scrollIntoViewIfNeeded()
    await page.locator('tbody tr').filter({ hasText: 'Cliente Ficticio 60' }).locator('img[alt="Foto de Cliente Ficticio 60"]').waitFor()
    assert.ok(fotosBaixadas.includes(idDe(60)), 'a linha 60 baixou ao chegar')
    await contexto.close()
  })))
await browser.close()
if (erros.length) falhas.push(`erros de página: ${erros.slice(0, 3).join(' | ')}`)
if (falhas.length) { console.log(`\n${falhas.length} falha(s)`); process.exit(1) }
console.log('\nFoto no grid de clientes: imagem, iniciais e carga preguiçosa aprovadas em 1920, 1440 e 390.')
