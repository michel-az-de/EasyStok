// Navegador → Vite proxy → Kestrel/TestAuth → PostgreSQL real. Sem interceptação HTTP.
// Só os adapters externos de frete usam o simulador explicitamente configurado na fixture.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { readFileSync, mkdirSync, writeFileSync } from 'node:fs'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const fixture = JSON.parse(readFileSync(process.env.LOGISTICA_FIXTURE ?? '../.build/logistica-browser.json', 'utf8'))
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5197'
const evidencias = process.env.EVIDENCIAS_UX ?? '../.build/1693-api-real'
mkdirSync(evidencias, { recursive: true })
async function ler(caminho) {
  const resposta = await fetch(fixture.api + caminho)
  const corpo = await resposta.json()
  assert.equal(resposta.ok, true, `${caminho}: ${resposta.status} ${JSON.stringify(corpo)}`)
  return corpo.data
}
const contextoAntes = await ler(`/api/atendimento/logistica/contexto?dia=${fixture.dia}`)
assert.equal(contextoAntes.demonstracao, true)
assert.equal(contextoAntes.pedidos.length, 10)
const ocupados = new Set(contextoAntes.despachos.filter(d => !['cancelado', 'devolvido'].includes(d.status)).flatMap(d => d.pedidoIds))
const pedido = contextoAntes.pedidos.find(p => p.status === 'pronto' && p.endereco && !ocupados.has(p.id))
assert.ok(pedido, 'A fixture precisa ter um pedido pronto, com endereço e sem contratação ativa.')
const movimentosAntes = await ler('/api/caixa/movimentos?pageSize=200&tipo=saida')
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, reducedMotion: 'reduce' })
await context.addInitScript(({ empresaId, usuarioId }) => sessionStorage.setItem('easystok.sessao', JSON.stringify({
  token: 'fixture-testauth-local', expiraEm: Date.now() + 3600000, empresa: { id: empresaId }, usuario: { id: usuarioId, nome: 'Gerente da demonstração' },
})), fixture)
const page = await context.newPage()
const erros = [], respostas = []
page.on('pageerror', e => erros.push(e.message))
page.on('response', r => { if (r.url().includes('/api/atendimento/logistica/')) respostas.push({ caminho: new URL(r.url()).pathname, status: r.status() }) })
page.setDefaultTimeout(15000)
try {
  await page.goto(origem + '/#/m/entregas')
  await page.getByLabel('Outro dia', { exact: true }).fill(fixture.dia)
  await page.getByRole('button', { name: 'Organizar transportes', exact: true }).click()
  const modal = page.getByRole('dialog', { name: 'Organizar entrega' })
  await modal.getByText('Ambiente de demonstração.', { exact: false }).waitFor()
  const selecao = modal.locator('details').first()
  if (!await selecao.getAttribute('open').then(v => v !== null)) await selecao.locator('summary').click()
  for (const p of contextoAntes.pedidos) {
    const campo = modal.getByLabel(`Pedido ${p.numero}, ${p.cliente}`, { exact: true })
    if (await campo.isChecked()) await campo.uncheck()
  }
  await modal.getByLabel(`Pedido ${pedido.numero}, ${pedido.cliente}`, { exact: true }).check()
  await modal.getByRole('button', { name: 'Comparar transportes', exact: true }).click()
  await modal.getByRole('button', { name: 'Escolher Lalamove', exact: true }).waitFor()
  await page.screenshot({ path: `${evidencias}/desktop-cotacao.png`, fullPage: true })
  await modal.getByRole('button', { name: 'Escolher Lalamove', exact: true }).click()
  await modal.getByRole('button', { name: 'Confirmar contratação simulada', exact: true }).click()
  await modal.getByText('Contratação registrada. Acompanhe a entrega abaixo.', { exact: true }).waitFor()
  const contratado = await ler(`/api/atendimento/logistica/contexto?dia=${fixture.dia}`)
  const despacho = contratado.despachos.find(d => d.pedidoIds.includes(pedido.id) && d.status === 'agendado')
  assert.ok(despacho, 'A contratação deve persistir e ser relida pela API.')
  const cartao = modal.locator(`[data-despacho-id="${despacho.id}"]`)
  await cartao.getByRole('button', { name: 'Registrar pagamento', exact: true }).click()
  const referencia = `prova-navegador-${despacho.id}`
  await cartao.getByLabel('Referência do pagamento').fill(referencia)
  await cartao.getByRole('button', { name: 'Confirmar registro' }).click()
  await modal.getByText('Pagamento registrado no caixa.', { exact: true }).waitFor()
  const pago = (await ler(`/api/atendimento/logistica/contexto?dia=${fixture.dia}`)).despachos.find(d => d.id === despacho.id)
  assert.equal(pago.pago, true)
  const movimentosDepois = await ler('/api/caixa/movimentos?pageSize=200&tipo=saida')
  const novos = movimentosDepois.filter(m => !movimentosAntes.some(a => a.id === m.id))
  assert.equal(novos.length, 1, 'Um único movimento financeiro deve nascer da confirmação.')
  assert.equal(novos[0].referencia, referencia)
  assert.equal(Math.round(novos[0].valor * 100), despacho.valorCentavos)
  const viagem = cartao
  await viagem.getByRole('button', { name: 'Simular saída', exact: true }).click()
  await viagem.getByRole('button', { name: 'Confirmar registro' }).click()
  await viagem.getByRole('button', { name: 'Simular entrega', exact: true }).click()
  await viagem.getByRole('button', { name: 'Confirmar registro' }).click()
  await modal.getByText('Entrega concluída.', { exact: true }).waitFor()
  await page.setViewportSize({ width: 390, height: 844 })
  await page.screenshot({ path: `${evidencias}/celular-entregue.png`, fullPage: true })
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false)
  const final = await ler(`/api/atendimento/logistica/contexto?dia=${fixture.dia}`)
  const despachoFinal = final.despachos.find(d => d.id === despacho.id)
  assert.equal(despachoFinal.status, 'entregue')
  assert.equal(final.pedidos.find(p => p.id === pedido.id).status, 'entregue')
  assert.match(despachoFinal.rastreioUrl, /^\/api\/entregas-demonstracao\//)
  const publico = await browser.newContext({ viewport: { width: 390, height: 844 } })
  const paginaPublica = await publico.newPage()
  await paginaPublica.goto(new URL(despachoFinal.rastreioUrl, origem).href)
  await paginaPublica.getByRole('heading', { name: 'Pedido entregue', exact: true }).waitFor()
  const textoPublico = await paginaPublica.locator('body').innerText()
  assert.ok(!textoPublico.includes(pedido.endereco))
  assert.ok(!textoPublico.includes(pedido.cliente))
  assert.ok(!textoPublico.includes('R$'))
  await paginaPublica.screenshot({ path: `${evidencias}/rastreio-publico.png`, fullPage: true })
  await publico.close()
  const tokenAlterado = despachoFinal.rastreioUrl.slice(0, -1) + (despachoFinal.rastreioUrl.endsWith('0') ? '1' : '0')
  assert.equal((await fetch(new URL(tokenAlterado, fixture.api))).status, 404)
  await page.reload()
  await page.getByLabel('Outro dia', { exact: true }).fill(fixture.dia)
  await page.getByRole('button', { name: 'Organizar transportes', exact: true }).click()
  await modal.getByText('Entrega concluída.', { exact: true }).waitFor()
  assert.deepEqual(erros, [])
  assert.ok(respostas.every(r => r.status === 200), JSON.stringify(respostas))
  writeFileSync(`${evidencias}/resultado.json`, JSON.stringify({ tipo: 'API e PostgreSQL reais; providers simulados; autenticação de teste', dia: fixture.dia,
    pedidoId: pedido.id, despachoId: despacho.id, movimentoId: novos[0].id, valorCentavos: despacho.valorCentavos, status: 'entregue', rastreioPublicoSemDadosPessoais: true, assinaturaAlteradaRecusada: true, respostas, erros }, null, 2))
  console.log('OK: navegador sem mock → API/PostgreSQL reais: contratação, saída única no caixa, status entregue no despacho e pedido, persistência após recarregar, desktop e celular. Provedores externos simulados.')
} catch (erro) {
  await page.screenshot({ path: `${evidencias}/falha.png`, fullPage: true })
  console.error(erros, (await page.locator('body').innerText()).slice(-1500))
  throw erro
} finally { await browser.close() }
