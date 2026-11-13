// Jornada pelos controles reais. As chamadas diretas abaixo são somente leituras de reconciliação.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const fixture = JSON.parse(readFileSync(process.env.R4_FIXTURE ?? '../.build/r4-browser.json', 'utf8'))
const origem = process.env.CONSOLE_TEST_URL
assert.ok(origem)
const rodada = Number(process.env.R4_JORNADA ?? 1)
const produto = fixture.produtos[(rodada - 1) % fixture.produtos.length]
assert.ok(produto)
const saldoInicial = Number(process.env.R4_SALDO_INICIAL ?? 8)
const pasta = resolve(import.meta.dirname, '../.build/2395/real')
mkdirSync(pasta, { recursive: true })
const estadoArquivo = resolve(pasta, `jornada-ui-${rodada}-estado.json`)
let prova = process.env.R4_CONTINUAR === '1' && existsSync(estadoArquivo) ? JSON.parse(readFileSync(estadoArquivo, 'utf8')) : { conversaId: fixture.conversaId, resultados: [] }
assert.equal(prova.conversaId, fixture.conversaId, 'Não reutilizar estado de outra fixture')
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: rodada === 1 ? 1440 : 390, height: rodada === 1 ? 1000 : 844 }, locale: 'pt-BR', reducedMotion: 'reduce' })
const page = await context.newPage()
page.setDefaultTimeout(25000)
let etapa = 'login'
const erros = []
page.on('pageerror', e => erros.push(e.message))
const salvar = () => writeFileSync(estadoArquivo, JSON.stringify(prova, null, 2))
const registrar = (caso, extra = {}) => { prova.resultados.push({ caso, ...extra }); salvar(); console.log(`ok ${caso}`) }
async function ler(caminho) {
  return page.evaluate(async caminho => {
    const s = JSON.parse(sessionStorage.getItem('easystok.sessao') ?? localStorage.getItem('easystok.sessao'))
    const r = await fetch(caminho, { headers: { Authorization: `Bearer ${s.token}` } })
    const texto = await r.text()
    if (!r.ok) throw new Error(`Leitura ${caminho}: ${r.status} ${texto}`)
    return texto ? JSON.parse(texto).data : null
  }, caminho)
}
const saldo = (p = produto) => ler(`/api/estoque/${p.itemEstoqueId}?empresaId=${fixture.empresaId}`).then(x => x.qty)
const detalhe = () => ler(`/api/pedidos/${prova.pedidoId}?empresaId=${fixture.empresaId}`)
async function respostaDaAcao(caminho, metodo, agir) {
  const aguardando = page.waitForResponse(r => new URL(r.url()).pathname === caminho && r.request().method() === metodo)
  await agir()
  const r = await aguardando
  const texto = await r.text()
  assert.ok(r.ok(), `${metodo} ${caminho}: ${r.status()} ${texto}`)
  if (metodo === 'POST' && caminho === `/api/atendimento/conversas/${fixture.conversaId}/pedido` && prova.identidadeProposta) {
    const corpo = r.request().postDataJSON()
    assert.deepEqual({ clienteId: corpo.clienteId, vinculoRevisao: corpo.vinculoRevisao }, prova.identidadeProposta, 'POST carrega a identidade conferida antes de montar a proposta')
  }
  return texto ? JSON.parse(texto).data : null
}
async function navegar(href) {
  const conversas = page.getByRole('tab', { name: /^Conversas\b/ })
  if (await conversas.count()) await conversas.click()
  let alvo = page.locator(`a[href="${href}"]:visible`).first()
  if (!await alvo.count()) {
    const menu = page.getByRole('button', { name: /^Menu de / })
    if (await menu.count()) await menu.click()
    alvo = page.locator(`a[href="${href}"]:visible`).first()
  }
  if (!await alvo.count()) {
    const inicio = page.locator('a[href="#/"]:visible').first()
    if (await inicio.count()) await inicio.click()
    alvo = page.locator(`a[href="${href}"]:visible`).first()
  }
  await alvo.click()
}
async function selecionarAna() {
  const conversas = page.getByRole('tab', { name: /^Conversas\b/ })
  if (await conversas.count()) await conversas.click()
  const todas = page.getByRole('button', { name: /^Todas\b/ })
  await todas.waitFor(); await todas.click()
  const concluido = page.getByRole('button', { name: /^Concluídas\b/ })
  const inbox = await ler('/api/atendimento/conversas?limite=100&pagina=1')
  const numero = inbox.find(c => c.id === fixture.conversaId)?.numeroComanda
  assert.ok(numero, 'Conversa da jornada identificada pelo ID da fixture')
  const ana = page.getByRole('button', { name: new RegExp(`^Ana Pereira.*Comanda ${numero},`) })
  await ana.or(concluido).first().waitFor()
  if (!await ana.isVisible() && await concluido.count()) await concluido.click()
  await ana.click()
}
async function ficha() {
  if (rodada > 1) {
    const aba = page.getByRole('tab', { name: /Ficha|Cliente|Pedido/ })
    if (await aba.count()) await aba.click()
  }
}
async function fecharPaineis() {
  for (const rotulo of [/^Fechar cardápio$/, /^Recolher Cardápio/, /^Recolher Comanda/]) {
    const botao = page.getByRole('button', { name: rotulo })
    if (await botao.isVisible()) await botao.click()
  }
}
async function cozinha() {
  await navegar('#/m/cozinha')
  await page.getByRole('radio', { name: 'Amanhã', exact: true }).click()
}
async function avancar(rotulo, estado) {
  await respostaDaAcao(`/api/kds/pedidos/${prova.pedidoId}/status`, 'PATCH', () => page.getByRole('button', { name: rotulo, exact: true }).click())
  prova.status = estado; salvar()
}
try {
  const dona = fixture.identidades.find(i => i.perfil === 'Dona' && i.empresaId === fixture.empresaId)
  await page.goto(origem + '/#/m/atendimento/balcao')
  await page.getByLabel('E-mail', { exact: true }).fill(dona.email)
  await page.getByLabel('Senha', { exact: true }).fill(dona.senha)
  await page.getByRole('button', { name: 'Entrar', exact: true }).click()
  await page.waitForFunction(() => {
    const sessao = sessionStorage.getItem('easystok.sessao') ?? localStorage.getItem('easystok.sessao')
    return sessao && JSON.parse(sessao).token
  })
  if (process.env.R4_SO_CLASSIFICACAO !== '1') {
  await selecionarAna()
  if (rodada > 1 && !prova.pedidoId) {
    const reabrir = page.getByRole('button', { name: 'Reabrir conversa', exact: true })
    if (await reabrir.isVisible()) await reabrir.click()
    await ficha()
    const nova = page.getByRole('button', { name: 'Nova comanda', exact: true })
    await nova.or(page.getByRole('button', { name: 'Adicionar itens', exact: true })).first().waitFor()
    if (await nova.isVisible()) await nova.click()
  }
  await ficha()
  if (!prova.pedidoId) {
    etapa = 'montagem'
    assert.equal(await saldo(), saldoInicial)
    const vinculo = await ler(`/api/atendimento/conversas/${fixture.conversaId}/vinculo-cliente`)
    prova.identidadeProposta = { clienteId: vinculo.clienteId, vinculoRevisao: vinculo.vinculoRevisao }
    salvar()
    await page.getByRole('button', { name: 'Adicionar itens', exact: true }).click()
    const somar = page.getByRole('button', { name: new RegExp(`^(Somar )?${produto.nome}`, 'i') }).first()
    await somar.click(); await somar.click()
    await fecharPaineis()
    await page.getByRole('button', { name: /^Revisar comanda/ }).first().click()
    await page.getByLabel('Dia da entrega (opcional)', { exact: true }).fill(fixture.dataEntrega)
    await page.getByRole('button', { name: /Manhã.*vagas? livres?/ }).first().click()
    await fecharPaineis()
    const dispensarAvisos = page.getByRole('button', { name: 'Dispensar avisos do aparelho', exact: true })
    if (await dispensarAvisos.isVisible()) { await dispensarAvisos.click(); prova.avisosDispensadosPeloControle = true; salvar() }
    await page.getByRole('button', { name: 'Gerar cobrança e enviar', exact: true }).click()
    await page.getByRole('radio', { name: 'Dinheiro na entrega', exact: true }).click()
    const pedido = await respostaDaAcao(`/api/atendimento/conversas/${fixture.conversaId}/pedido`, 'POST', () => page.getByRole('button', { name: 'Confirmar e enviar', exact: true }).click())
    prova.pedidoId = pedido.pedidoId; prova.total = pedido.total; prova.status = 'aguardando'; salvar()
    assert.equal(await saldo(), saldoInicial)
    registrar(`Pedido criado com duas lasanhas pela interface, estoque permanece ${saldoInicial}`, { pedidoId: prova.pedidoId, total: prova.total, produtoId: produto.produtoId })
  }
  etapa = 'producao'
  await fecharPaineis()
  if (['aguardando', 'preparando'].includes(prova.status)) {
    await cozinha()
    if (prova.status === 'aguardando') { await avancar('Começar preparo', 'preparando'); assert.equal(await saldo(), saldoInicial) }
    await avancar('Marcar pronto', 'pronto')
    assert.equal(await saldo(), saldoInicial - 2)
    await page.reload()
    assert.equal(await saldo(), saldoInicial - 2)
    registrar(`KDS Começar preparo mantém ${saldoInicial}; Marcar pronto baixa para ${saldoInicial - 2}, confirmado após recarga`)
  }
  etapa = 'recebimento'
  if (!prova.pago) {
    await navegar('#/m/atendimento')
    await selecionarAna(); await ficha()
    await page.getByRole('button', { name: /^Pagamento\b/ }).first().click()
    const modal = page.getByRole('dialog').last()
    await modal.getByRole('button', { name: 'Marcar pago', exact: true }).click()
    await modal.getByLabel('Como recebeu', { exact: true }).selectOption('dinheiro')
    await respostaDaAcao(`/api/pedidos/${prova.pedidoId}/pagamentos`, 'POST', () => modal.getByRole('button', { name: 'Marcar pago', exact: true }).click())
    const recebido = await detalhe()
    assert.equal(recebido.pedido.totalPago, prova.total)
    assert.equal(recebido.pagamentos.length, 1)
    prova.pago = true; salvar()
    registrar('Recebimento manual confirmado pela UI e um pagamento persistido', { totalPago: recebido.pedido.totalPago })
    await modal.getByRole('button', { name: 'Fechar janela', exact: true }).click()
  }
  etapa = 'entrega'
  if (prova.status !== 'entregue') {
    await cozinha()
    if (prova.status === 'pronto') await avancar('Saiu para entrega', 'saiu_para_entrega')
    await avancar('Marcar entregue', 'entregue')
    assert.equal(await saldo(), saldoInicial - 2)
    registrar(`Saída e entrega pelos controles, saldo segue ${saldoInicial - 2}`)
  }
  etapa = 'comunicacao-encerramento'
  await navegar('#/m/atendimento')
  await selecionarAna()
  if (rodada > 1) await page.getByRole('tab', { name: 'Mensagens', exact: true }).click()
  const texto = `Ensaio R4 ${rodada}: entrega conferida. Obrigada pela preferência.`
  await page.getByRole('textbox', { name: 'Mensagem para o cliente' }).fill(texto)
  await respostaDaAcao(`/api/atendimento/conversas/${fixture.conversaId}/mensagens`, 'POST', () => page.getByRole('button', { name: 'Enviar mensagem', exact: true }).click())
  await page.getByRole('button', { name: 'Mais opções da conversa', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Concluir conversa', exact: true }).click()
  const encerramento = page.getByRole('dialog', { name: 'Concluir conversa', exact: true })
  await encerramento.getByRole('checkbox', { name: 'Mandar mensagem de encerramento ao cliente', exact: true }).uncheck()
  await respostaDaAcao(`/api/atendimento/conversas/${fixture.conversaId}/encerrar`, 'POST', () => encerramento.getByRole('button', { name: 'Concluir conversa', exact: true }).click())
  await page.reload()
  const final = await detalhe()
  assert.equal(final.pedido.totalPago, prova.total)
  assert.equal(await saldo(), saldoInicial - 2)
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
  await page.screenshot({ path: resolve(pasta, `jornada-ui-${rodada}.png`), fullPage: true })
  registrar('Comunicação controlada e encerramento pela UI; recebimento e estoque preservados após reload')
  } else {
    const entregue = await detalhe()
    assert.equal(entregue.pedido.status, 'entregue')
    assert.equal(entregue.pedido.totalPago, prova.total)
    assert.equal(entregue.pagamentos.length, 1)
  }
  const cadastro = await ler(`/api/clientes/${fixture.clienteId}?empresaId=${fixture.empresaId}`)
  assert.ok(cadastro.cliente.orderCount > 0)
  await page.goto(`${origem}/#/m/cadastros`)
  await page.getByRole('button', { name: 'Clientes', exact: true }).click()
  const linhaComprador = page.getByRole('row').filter({ has: page.getByRole('button', { name: 'Editar Ana Pereira', exact: true }) })
  await linhaComprador.getByText('Cliente', { exact: true }).waitFor()
  await respostaDaAcao('/api/clientes', 'GET', () => page.getByRole('button', { name: 'Leads', exact: true }).click())
  await page.getByRole('button', { name: 'Editar Ana Pereira', exact: true }).waitFor({ state: 'hidden' })
  registrar('Após reload, comprador aparece em Clientes e deixa a lista de Leads')
  assert.deepEqual(erros, [])
  delete prova.falha
} catch (erro) {
  prova.falha = { etapa, mensagem: erro.message }
  await page.screenshot({ path: resolve(pasta, `jornada-ui-${rodada}-falha.png`), fullPage: true })
  writeFileSync(resolve(pasta, `jornada-ui-${rodada}-falha.txt`), (await page.locator('body').innerText()).slice(0, 18000))
  throw erro
} finally {
  prova.escopo = 'Controles reais do Console/API/JWT/PostgreSQL; provedores externos controlados na fixture; leituras API só reconciliam resultados'
  prova.erros = erros; salvar()
  await context.close(); await browser.close()
}
