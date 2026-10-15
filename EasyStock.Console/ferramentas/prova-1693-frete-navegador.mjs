// Console real com HTTP controlado: valida UX/contrato, não homologa provedores externos.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5196'
const evidencias = process.env.EVIDENCIAS_UX ?? '../.build/1693-ux'
mkdirSync(evidencias, { recursive: true })
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, reducedMotion: 'reduce' })
const erros = [], chaves = [], escritas = [], chamadasLalamove = []
context.on('request', r => { if (new URL(r.url()).hostname === 'web.lalamove.com') chamadasLalamove.push(r.url()) })
const pedidos = Array.from({ length: 10 }, (_, i) => ({ id: `00000000-0000-4000-8000-${String(i + 1).padStart(12, '0')}`, numero: `P${i + 1}`, cliente: `Cliente ${i + 1}`, endereco: `Destino de teste ${i + 1}`, bairro: `Região ${i % 3}`, telefone: '+5500000000000', latitude: -23.55, longitude: -46.63, clienteBloqueado: false, janelaInicio: '2026-10-13T13:00:00Z', janelaFim: '2026-10-13T14:00:00Z', status: 'pronto', freteCentavos: 500 }))
let despachos = [], cotados = [], falharContratacao = true, gerente = true, ultimoPedidoCotacao = null
let empresaReal = false, falharContexto = false
let politica = { automatico: false, tetoCentavos: 4000, permitirAgrupar: true }
await context.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova-local', expiraEm: Date.now() + 3600000, empresa: { id: '11111111-1111-4111-8111-111111111111' }, usuario: { id: '22222222-2222-4222-8222-222222222222', nome: 'Revisora' } })))
await context.route('**/api/**', async (route) => {
  const r = route.request(), path = new URL(r.url()).pathname
  if (!path.startsWith('/api/')) return route.continue()
  const ok = (data) => route.fulfill({ json: { data } })
  const body = r.method() === 'GET' ? null : r.postDataJSON()
  if (r.method() !== 'GET') escritas.push(path)
  if (path.endsWith('/logistica/contexto')) {
    if (falharContexto) return route.fulfill({ status: 503, json: { error: { message: 'Serviço indisponível temporariamente.' } } })
    return ok(empresaReal ? { demonstracao: false, politica: { automatico: false }, pedidos: [], despachos: [], indisponibilidade: 'A contratação de transporte ainda não está conectada nesta empresa. Consulte o prestador e combine o frete com o cliente.' }
      : { demonstracao: true, politica, pedidos, despachos })
  }
  if (path.endsWith('/logistica/cotacoes')) {
    ultimoPedidoCotacao = body
    cotados = body.pedidoIds
    return ok([{ id: 'q1', provedor: 'lalamove', valorCentavos: 2300, duracaoMinutos: 35, expiraEm: '2026-10-13T13:15:00Z', simulado: true, indisponibilidade: null, pedidoIds: cotados }, { id: 'q2', provedor: 'uber', valorCentavos: 2700, duracaoMinutos: 25, expiraEm: '2026-10-13T13:15:00Z', simulado: true, indisponibilidade: null, pedidoIds: cotados }])
  }
  if (path.endsWith('/logistica/despachos') && r.method() === 'POST') {
    chaves.push(body.chaveIdempotencia)
    if (falharContratacao) { falharContratacao = false; return route.fulfill({ status: 503, json: { error: { message: 'Resposta perdida. Repita a confirmação.' } } }) }
    const d = { id: 'd1', provedor: 'lalamove', status: 'agendado', valorCentavos: 2300, pago: false, simulado: true, rastreioUrl: null, pedidoIds: cotados, criadoEm: new Date().toISOString(), eventos: [] }
    despachos = [d]; return ok(d)
  }
  if (path.endsWith('/d1/pagamento')) { assert.equal(body.meio, 'pix'); despachos[0].pago = true; return ok(despachos[0]) }
  if (path.endsWith('/d1/evento')) { despachos[0].status = body.tipo; despachos[0].eventos.push({ tipo: body.tipo, motivo: body.motivo, em: new Date().toISOString() }); return ok(despachos[0]) }
  if (path.endsWith('/logistica/politica')) { politica = body; return ok(politica) }
  if (path === '/api/auth/me/modulos') return ok({ modulos: ['entregas', 'atendimento', 'financeiro'].map(id => ({ id, liberado: true })), acoes: { gerenciarCaixa: gerente, editarAtendimento: gerente } })
  if (path === '/api/auth/google/config') return ok({ habilitado: false })
  if (path === '/api/notificacoes/badge') return ok({ count: 0 })
  if (r.method() !== 'GET') throw new Error(`Escrita inesperada ${path}`)
  return ok([])
})
const page = await context.newPage()
page.on('pageerror', e => erros.push(e.message)); page.on('console', m => { if(m.type()==='error') console.error(m.text()) }); page.on('requestfailed', r => console.error(r.url(), r.failure()))
page.setDefaultTimeout(10000)
try {
  await page.goto(origem + '/#/m/entregas')
  await page.getByRole('button', { name: 'Organizar transportes', exact: true }).click()
  const modal = page.getByRole('dialog', { name: 'Organizar entrega' })
  await modal.getByText('Ambiente de demonstração.', { exact: false }).waitFor()
  assert.equal(await modal.getByRole('checkbox').count(), 12)
  await modal.getByLabel('Pedido P1, Cliente 1', { exact: true }).check()
  await modal.getByLabel('Pedido P2, Cliente 2', { exact: true }).check()
  await modal.getByLabel('Incluir entregador combinado e entrega em mãos').check()
  await modal.getByLabel('Custo combinado com o entregador (R$, opcional)').fill('-1')
  assert.equal(await modal.getByRole('button', { name: 'Comparar transportes', exact: true }).isDisabled(), true)
  await modal.getByLabel('Custo combinado com o entregador (R$, opcional)').fill('19.90')
  await modal.getByRole('button', { name: 'Comparar transportes', exact: true }).click()
  await modal.getByRole('button', { name: 'Escolher Lalamove', exact: true }).waitFor()
  assert.equal(ultimoPedidoCotacao.valorManualCentavos, 1990)
  assert.equal(ultimoPedidoCotacao.incluirManual, true)
  await page.screenshot({ path: `${evidencias}/desktop-cotacoes.png`, fullPage: true })
  await modal.getByRole('button', { name: 'Escolher Lalamove', exact: true }).click()
  await modal.getByRole('button', { name: 'Confirmar contratação simulada' }).click()
  await modal.getByRole('alert').filter({ hasText: 'Resposta perdida' }).waitFor()
  await modal.getByRole('button', { name: 'Confirmar contratação simulada' }).click()
  await modal.getByText('Contratação registrada.', { exact: false }).waitFor()
  assert.equal(chaves.length, 2); assert.equal(chaves[0], chaves[1])
  await modal.getByRole('button', { name: 'Registrar pagamento', exact: true }).click()
  await modal.getByLabel('Referência do pagamento').fill('Pix confirmado no teste')
  await modal.getByRole('button', { name: 'Confirmar registro' }).click()
  await modal.getByText('Pagamento registrado no caixa.', { exact: true }).waitFor()
  await modal.getByRole('button', { name: 'Simular saída' }).click()
  await modal.getByRole('button', { name: 'Confirmar registro' }).click()
  await modal.getByRole('button', { name: 'Registrar problema' }).click()
  await modal.getByLabel('Motivo ou observação').fill('Cliente ausente')
  await modal.getByRole('button', { name: 'Confirmar registro' }).click()
  await modal.getByRole('button', { name: 'Iniciar retorno à loja' }).click()
  await modal.getByLabel('Motivo ou observação').fill('Cliente pediu retorno')
  await modal.getByRole('button', { name: 'Confirmar registro' }).click()
  await modal.getByRole('button', { name: 'Confirmar retorno à loja' }).click()
  await modal.getByRole('button', { name: 'Confirmar registro' }).click()
  await modal.getByText('Lalamove · Devolvido à loja', { exact: true }).waitFor()
  assert.equal(despachos[0].eventos.length, 4)
  await page.setViewportSize({ width: 390, height: 844 })
  await page.screenshot({ path: `${evidencias}/celular-retorno.png`, fullPage: true })
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false)
  await page.keyboard.press('Escape')
  await modal.waitFor({ state: 'hidden' })
  assert.equal(await page.getByRole('button', { name: 'Organizar transportes', exact: true }).evaluate(e => e === document.activeElement), true)
  gerente = false
  despachos[0].pago = false
  await page.reload()
  await page.getByRole('button', { name: 'Organizar transportes', exact: true }).click()
  await modal.getByText('Lalamove · Devolvido à loja', { exact: true }).waitFor()
  assert.equal(await modal.getByRole('button', { name: 'Registrar pagamento', exact: true }).count(), 0)
  assert.equal(await modal.getByText('Preferências de contratação', { exact: true }).count(), 0)
  await page.keyboard.press('Escape')
  await page.setViewportSize({ width: 1366, height: 900 })
  empresaReal = true
  const escritasAntes = escritas.length
  await page.getByRole('button', { name: 'Organizar transportes', exact: true }).click()
  const orientacao = modal.getByRole('region', { name: 'Combinar transporte com o prestador' })
  await orientacao.waitFor()
  assert.equal(await orientacao.evaluate(e => e === document.activeElement), true)
  assert.equal(await modal.getByRole('alert').count(), 0)
  assert.equal(await modal.getByRole('button', { name: 'Tentar novamente' }).count(), 0)
  assert.equal(await modal.getByRole('button', { name: 'Comparar transportes' }).count(), 0)
  assert.doesNotMatch(await modal.innerText(), /laboratório|demonstração/iu)
  assert.match(await orientacao.innerText(), /Com o cliente de acordo, revise a seleção e use Criar pedido no Atendimento/)
  const consultaLalamove = modal.getByRole('link', { name: 'Consultar Lalamove (site externo, abre em nova aba)', exact: true })
  assert.equal(await consultaLalamove.getAttribute('href'), 'https://web.lalamove.com/')
  assert.equal(await consultaLalamove.getAttribute('target'), '_blank')
  assert.equal(await consultaLalamove.getAttribute('rel'), 'noopener noreferrer')
  assert.deepEqual(chamadasLalamove, [])
  assert.equal(await modal.getByRole('link', { name: 'Abrir Entregas' }).getAttribute('href'), '#/m/entregas')
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false)
  await page.screenshot({ path: `${evidencias}/desktop-empresa-real.png`, fullPage: true })
  await modal.getByRole('link', { name: 'Abrir Entregas' }).click()
  await modal.waitFor({ state: 'hidden' })
  assert.equal(new URL(page.url()).hash, '#/m/entregas')
  assert.equal(escritas.length, escritasAntes)

  falharContexto = true
  await page.getByRole('button', { name: 'Organizar transportes', exact: true }).click()
  await modal.getByRole('alert').filter({ hasText: 'Serviço indisponível temporariamente.' }).waitFor()
  assert.equal(await orientacao.count(), 0)
  falharContexto = false
  await modal.getByRole('button', { name: 'Tentar novamente' }).click()
  await orientacao.waitFor()
  assert.equal(await orientacao.evaluate(e => e === document.activeElement), true)
  await page.keyboard.press('Escape')

  empresaReal = false
  pedidos[0].latitude = null
  await page.getByRole('button', { name: 'Organizar transportes', exact: true }).click()
  await modal.getByText('Ambiente de demonstração.', { exact: false }).waitFor()
  const pedidosParaCotar = modal.locator('details').first()
  if (await pedidosParaCotar.getAttribute('open') === null) await pedidosParaCotar.locator('summary').click()
  await modal.getByText('Localização não validada.', { exact: false }).waitFor()
  const semLocalizacao = modal.getByLabel('Pedido P1, Cliente 1', { exact: true })
  assert.equal(await semLocalizacao.isDisabled(), true)
  assert.ok(await semLocalizacao.getAttribute('aria-describedby'))
  await modal.getByLabel('Pedido P3, Cliente 3', { exact: true }).check()
  assert.equal(await modal.getByRole('button', { name: 'Comparar transportes', exact: true }).isEnabled(), true)
  await page.screenshot({ path: `${evidencias}/desktop-requisitos.png`, fullPage: true })
  assert.deepEqual(erros, [])
  console.log('OK: laboratório; empresa real orientada sem escritas; falha de rede com retentativa; localização pendente descrita; cotação/manual; idempotência; pagamento; saída/retorno; Escape/foco; permissões. HTTP controlado, não integração externa.')
} catch (erro) {
  console.error(erros); console.error((await page.locator('body').innerText()).slice(0, 1800))
  throw erro
} finally { await browser.close() }
