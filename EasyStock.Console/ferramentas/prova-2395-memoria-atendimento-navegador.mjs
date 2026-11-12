// App real; armazenamento de rascunhos bloqueado por instrumentação, HTTP controlado.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, `../.build/2395/memoria-atendimento/${process.env.R4_ETAPA_PROVA ?? 'green'}`)
mkdirSync(pasta, { recursive: true })
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 900 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => {
  sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja da memória' }, usuario: { id: 'dona', nome: 'Dona' } }))
  const gravar = Storage.prototype.setItem
  Storage.prototype.setItem = function(chave, valor) {
    if (chave.startsWith('easystok.rascunhos:') && !sessionStorage.getItem('r4.quota-liberada')) throw new DOMException('Quota de teste', 'QuotaExceededError')
    return gravar.call(this, chave, valor)
  }
})
const conversas = Array.from({ length: 35 }, (_, i) => ({ id: `c${i}`, contatoNome: `Pessoa ${String(i).padStart(2, '0')}`, contatoIdExterno: `551199999${String(i).padStart(4, '0')}`, canal: 'WhatsApp', situacao: 'Assumida', assumidaPorUsuarioId: 'dona', ultimaMensagemEm: new Date(Date.now() - i * 60000).toISOString(), naoLidas: 0 }))
const chamadas = [], erros = [], resultado = {}
await contexto.route('**/api/**', route => {
  const path = new URL(route.request().url()).pathname
  if (!path.startsWith('/api/')) return route.continue()
  chamadas.push({ path, metodo: route.request().method() })
  const ok = data => route.fulfill({ json: { data } })
  if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros'].map(id => ({ id, liberado: true })), acoes: {} })
  if (path === '/api/atendimento/conversas') return ok(conversas)
  if (path.endsWith('/mensagens')) return ok([{ id: `${path}-m`, direcao: 'Entrada', texto: 'Bom dia.', enviadaEm: new Date().toISOString(), tipoConteudo: 'Texto' }])
  if (path.endsWith('/pedido')) return ok(null)
  if (path.endsWith('/dossie')) return ok({ cliente: null, enderecos: [], totalPedidos: 0, notas: [], tags: [] })
  if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
  if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
  if (path === '/api/atendimento/comanda/cardapio') return ok({ itens: [], porcoes: {} })
  return ok([])
})
const page = await contexto.newPage(); page.setDefaultTimeout(12000); page.on('pageerror', e => erros.push(e.message))
const url = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/#/m/atendimento/balcao`
const hash = async valor => { await page.evaluate(v => { location.hash = v }, valor) }
try {
  await page.goto(url)
  await page.getByRole('button', { name: /^Todas/ }).click()
  const busca = page.getByPlaceholder('Buscar conversa'); await busca.fill('Pessoa')
  await page.getByText('Pessoa 00', { exact: true }).first().waitFor()
  const lista = page.locator('[class*="rolagem"]').filter({ has: page.getByText('Pessoa 00', { exact: true }) }).first()
  await lista.hover(); await page.mouse.wheel(0, 700); await page.waitForTimeout(200)
  const antes = await lista.evaluate(el => el.scrollTop); assert.ok(antes >= 400)
  await hash('#/'); await page.getByPlaceholder(/insumos, caixa/).waitFor(); await hash('#/m/atendimento/balcao')
  await busca.waitFor(); await page.waitForTimeout(300)
  resultado.buscaAposVoltar = await busca.inputValue()
  resultado.posicaoAposVoltar = await lista.count() ? await lista.evaluate(el => el.scrollTop) : 0
  await page.reload(); await busca.waitFor(); await page.waitForTimeout(300)
  resultado.buscaAposReload = await busca.inputValue()
  resultado.posicaoAposReload = await lista.count() ? await lista.evaluate(el => el.scrollTop) : 0
  await page.screenshot({ path: resolve(pasta, 'filtros-posicao.png'), fullPage: true })
  await page.getByRole('button', { name: /^Todas/ }).click(); await busca.fill('Pessoa')
  await page.getByText('Pessoa 10', { exact: true }).first().click()
  const campo = page.getByPlaceholder(/^Escreva para/)
  const texto = 'Rascunho inteiro sem comanda. Linha 1.\nLinha 2 com acento: João.'
  await campo.fill(texto); await campo.blur(); await page.waitForTimeout(400)
  const aviso = page.getByRole('alert').filter({ hasText: 'Não foi possível guardar o texto das conversas' })
  resultado.aviso = await aviso.count()
  resultado.comandas = await page.evaluate(() => Object.keys(JSON.parse(sessionStorage.getItem('easystok.visita:empresa:dona') ?? '{}').comandas ?? {}).length)
  await page.setViewportSize({ width: 390, height: 844 }); await page.getByRole('tab', { name: 'Mensagens', exact: true }).click(); await page.screenshot({ path: resolve(pasta, 'texto-sem-storage-390.png'), fullPage: true })
  await hash('#/'); await page.getByPlaceholder(/insumos, caixa/).waitFor(); await hash('#/m/atendimento/balcao')
  await page.getByRole('tab', { name: 'Mensagens', exact: true }).click(); await campo.waitFor()
  resultado.textoAposVoltarSemStorage = await campo.inputValue()
  await page.evaluate(() => sessionStorage.setItem('r4.quota-liberada', '1'))
  const repetir = page.getByRole('button', { name: 'Tentar guardar o texto', exact: true })
  if (await repetir.count()) { await repetir.click(); await aviso.waitFor({ state: 'detached' }) }
  await page.reload(); await page.getByRole('tab', { name: 'Mensagens', exact: true }).click(); await campo.waitFor()
  resultado.textoAposRetryReload = await campo.inputValue()
  await page.screenshot({ path: resolve(pasta, 'texto-restaurado-390.png'), fullPage: true })
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ appReal: true, httpControlado: true, ...resultado, erros }, null, 2))
  assert.equal(resultado.buscaAposVoltar, 'Pessoa'); assert.equal(resultado.buscaAposReload, 'Pessoa')
  assert.ok(resultado.posicaoAposVoltar >= 400); assert.ok(resultado.posicaoAposReload >= 400)
  assert.equal(resultado.aviso, 1); assert.equal(resultado.comandas, 0)
  assert.equal(resultado.textoAposVoltarSemStorage, texto); assert.equal(resultado.textoAposRetryReload, texto)
  assert.ok(chamadas.every(c => c.metodo === 'GET' || c.path.endsWith('/marcar-lida'))); assert.deepEqual(erros, [])
  console.log('Memória Atendimento: filtros/posição ida/reload; quota sótexto, aviso, preservação e retry/reload integral passaram.')
} catch(e) { await page.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); throw e }
finally { await contexto.close(); await browser.close() }
