// Complemento J04/E02/U02: somente controles reais escrevem; fetch direto apenas lê.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const fixture = JSON.parse(readFileSync('../.build/r4-browser.json', 'utf8'))
const origem = process.env.CONSOLE_TEST_URL; assert.ok(origem)
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origem).hostname), 'Console deve ser local')
assert.ok(['localhost', '127.0.0.1'].includes(new URL(fixture.api).hostname), 'API deve ser local')
assert.match(fixture.provedores, /controlados/, 'Fixture deve usar provedores controlados')
const conversaId = fixture.conversaConferenciaId, produto = fixture.produtos[0]
const pasta = resolve(import.meta.dirname, '../.build/2395/real/entregas'); mkdirSync(pasta, { recursive: true })
const arquivo = resolve(pasta, 'resultado.json')
const prova = process.env.R4_CONTINUAR === '1' && existsSync(arquivo) ? JSON.parse(readFileSync(arquivo, 'utf8')) : { conversaId, resultados: [] }
assert.equal(prova.conversaId, conversaId)
const salvar = () => writeFileSync(arquivo, JSON.stringify(prova, null, 2))
const registrar = (caso, dados = {}) => { prova.resultados.push({ caso, ...dados }); salvar(); console.log(`ok ${caso}`) }
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'pt-BR', reducedMotion: 'reduce' })
const page = await context.newPage(); page.setDefaultTimeout(30000)
const erros = []; page.on('pageerror', e => erros.push(e.message)); let etapa = 'login'
const pessoa = fixture.identidades.find(i => i.perfil === 'Dona' && i.empresaId === fixture.empresaId)
async function ler(caminho) { return page.evaluate(async caminho => {
  const sessao = JSON.parse(sessionStorage.getItem('easystok.sessao') ?? localStorage.getItem('easystok.sessao'))
  const r = await fetch(caminho, { headers: { Authorization: `Bearer ${sessao.token}` } }); const t = await r.text()
  if (!r.ok) throw new Error(`GET ${caminho}: ${r.status} ${t}`)
  return t ? JSON.parse(t).data : null
}, caminho) }
const saldo = () => ler(`/api/estoque/${produto.itemEstoqueId}?empresaId=${fixture.empresaId}`).then(x => x.qty)
const detalhe = () => ler(`/api/pedidos/${prova.pedidoId}?empresaId=${fixture.empresaId}`)
async function agir(caminho, metodo, gesto) {
  const aguardando = page.waitForResponse(r => new URL(r.url()).pathname === caminho && r.request().method() === metodo)
  await gesto(); const r = await aguardando; const t = await r.text(); assert.ok(r.ok(), `${metodo} ${caminho}: ${r.status()} ${t}`)
  return t ? JSON.parse(t).data : null
}
async function navegar(href) {
  let a = page.locator(`a[href="${href}"]:visible`).first()
  if (!await a.count()) { const m = page.getByRole('button', { name: /^Menu de / }); if (await m.count()) await m.click() }
  a = page.locator(`a[href="${href}"]:visible`).first()
  if (!await a.count()) { await page.locator('a[href="#/"]:visible').first().click(); a = page.locator(`a[href="${href}"]:visible`).first() }
  await a.click()
}
async function selecionar() {
  await page.getByRole('button', { name: /^Todas\b/ }).click()
  const inbox = await ler('/api/atendimento/conversas?limite=100&pagina=1')
  prova.numeroComanda = inbox.find(c => c.id === conversaId)?.numeroComanda; assert.ok(prova.numeroComanda); salvar()
  await page.getByPlaceholder('Buscar conversa', { exact: true }).fill(`comanda ${prova.numeroComanda}`)
  await page.getByRole('button', { name: new RegExp(`Comanda ${prova.numeroComanda},`) }).click()
}
async function fecharPaineis() { for (const name of [/^Fechar cardápio$/, /^Recolher Cardápio/, /^Recolher Comanda/]) { const b = page.getByRole('button', { name }); if (await b.isVisible()) await b.click() } }
async function cozinha() { await navegar('#/m/cozinha'); await page.getByRole('radio', { name: 'Amanhã', exact: true }).click() }
async function avancar(nome, status) {
  const card = page.getByRole('listitem').filter({ hasText: new RegExp(`Comanda ${prova.numeroComanda}(?=nº|\\s|$)`) }).filter({ has: page.getByRole('button', { name: 'Ver comanda', exact: true }) })
  await agir(`/api/kds/pedidos/${prova.pedidoId}/status`, 'PATCH', () => card.getByRole('button', { name: nome, exact: true }).click())
  prova.status = status; salvar()
}
async function entregas() { await navegar('#/m/entregas'); await page.getByRole('tab', { name: 'Painel do dia', exact: true }).click(); await page.getByRole('radio', { name: 'Amanhã', exact: true }).click() }
const cartaoDia = () => page.getByRole('listitem').filter({ hasText: new RegExp(`Comanda ${prova.numeroComanda}(?=nº|\\s|$)`) }).filter({ has: page.getByRole('button', { name: 'Alterar agendamento', exact: true }) })
try {
  await page.goto(origem + '/#/m/atendimento/balcao'); await page.getByLabel('E-mail', { exact: true }).fill(pessoa.email)
  await page.getByLabel('Senha', { exact: true }).fill(pessoa.senha); await page.getByRole('button', { name: 'Entrar', exact: true }).click(); await selecionar()
  if (!prova.pedidoId) {
    etapa = 'nova-comanda'
    prova.saldoInicial = await saldo(); assert.ok(prova.saldoInicial >= 1)
    await page.getByRole('button', { name: 'Nova comanda', exact: true }).click()
    await page.getByRole('button', { name: 'Adicionar itens', exact: true }).click()
    await page.getByRole('button', { name: new RegExp(`^(Somar )?${produto.nome}`, 'i') }).first().click(); await fecharPaineis()
    await page.getByRole('button', { name: /^Revisar comanda/ }).first().click()
    await page.getByLabel('Dia da entrega (opcional)', { exact: true }).fill(fixture.dataEntrega)
    await page.getByRole('button', { name: /Manhã.*vagas? livres?/ }).first().click(); await fecharPaineis()
    const avisos = page.getByRole('button', { name: 'Dispensar avisos do aparelho', exact: true }); if (await avisos.isVisible()) await avisos.click()
    await page.getByRole('button', { name: 'Gerar cobrança e enviar', exact: true }).click()
    await page.getByRole('radio', { name: 'Dinheiro na entrega', exact: true }).click()
    page.once('dialog', d => d.type() === 'confirm' && /Confirme para assumir/.test(d.message()) ? d.accept() : d.dismiss())
    const pedido = await agir(`/api/atendimento/conversas/${conversaId}/pedido`, 'POST', () => page.getByRole('button', { name: 'Confirmar e enviar', exact: true }).click())
    prova.pedidoId = pedido.pedidoId; prova.total = pedido.total; prova.status = 'aguardando'; salvar()
    registrar('Nova intenção pela UI: uma bolonhesa, estoque ainda sem baixa', { pedidoId: prova.pedidoId, total: prova.total, saldo: await saldo() })
  }
  etapa = 'reagendamento-entregas'
  if (!prova.reagendado) {
    await fecharPaineis(); await entregas(); await cartaoDia().waitFor()
    prova.agendaAntes = await ler(`/api/pedidos/${prova.pedidoId}/janelas?dataInicio=${fixture.dataEntrega}&dataFim=${fixture.dataEntrega}`)
    await cartaoDia().getByRole('button', { name: 'Alterar agendamento', exact: true }).click()
    const modal = page.getByRole('dialog').last(); await modal.getByLabel('Dia da nova entrega (opcional)', { exact: true }).fill(fixture.dataEntrega)
    const select = modal.getByLabel('Nova janela de entrega', { exact: true })
    await select.selectOption(await select.locator('option').filter({ hasText: 'Tarde' }).getAttribute('value'))
    await agir(`/api/pedidos/${prova.pedidoId}/janela`, 'PATCH', () => modal.getByRole('button', { name: 'Confirmar agendamento', exact: true }).click())
    prova.reagendado = true; salvar()
    await modal.getByRole('button', { name: 'Fechar janela', exact: true }).click()
  }
  if (!prova.agendaConferida) {
    await entregas(); const tarde = page.getByRole('region', { name: /Janela.*Tarde/ }); await tarde.getByText(new RegExp(`Comanda ${prova.numeroComanda}(?=nº|\\s|$)`)).first().waitFor()
    prova.agendaDepois = await ler(`/api/pedidos/${prova.pedidoId}/janelas?dataInicio=${fixture.dataEntrega}&dataFim=${fixture.dataEntrega}`)
    assert.notDeepEqual(prova.agendaAntes.atual, prova.agendaDepois.atual)
    await page.screenshot({ path: resolve(pasta, 'entregas-reagendada.png'), fullPage: true })
    await cozinha(); const c = page.getByRole('listitem').filter({ hasText: new RegExp(`Comanda ${prova.numeroComanda}(?=nº|\\s|$)`) }).filter({ has: page.getByRole('button', { name: 'Ver comanda', exact: true }) })
    await c.getByText(/Tarde/).waitFor(); await navegar('#/m/atendimento'); await selecionar(); await page.getByText(/13:00 às 16:00|13h–16h/).first().waitFor()
    await page.getByRole('textbox', { name: 'Mensagem para o cliente' }).fill('Complemento R4: nova entrega confirmada para a tarde, conforme combinado.')
    await agir(`/api/atendimento/conversas/${conversaId}/mensagens`, 'POST', () => page.getByRole('button', { name: 'Enviar mensagem', exact: true }).click())
    prova.agendaConferida = true; salvar(); registrar('E02 reagendamento pelo módulo Entregas refletido em Cozinha e Atendimento antes de comunicar')
  }
  etapa = 'preparo'
  if (prova.status === 'aguardando') { await cozinha(); await avancar('Começar preparo', 'preparando') }
  if (prova.status === 'preparando') { await cozinha(); await avancar('Marcar pronto', 'pronto'); assert.equal(await saldo(), prova.saldoInicial - 1) }
  etapa = 'responsavel-viagem'
  await entregas()
  if (!prova.entregadorId) {
    await page.getByRole('tab', { name: 'Entregadores', exact: true }).click()
    await page.getByLabel('Nome', { exact: true }).fill('Responsável fictício R4'); await page.getByLabel('Veículo', { exact: true }).fill('Moto de teste')
    const e = await agir('/api/atendimento/entregadores', 'POST', () => page.getByRole('button', { name: 'Cadastrar entregador', exact: true }).click())
    prova.entregadorId = e.id; assert.ok(prova.entregadorId); salvar(); await entregas()
  }
  if (!prova.viagemId) { const v = await agir('/api/atendimento/viagens', 'POST', () => page.getByRole('button', { name: 'Nova viagem', exact: true }).click()); prova.viagemId = v.id; salvar() }
  if (!prova.emRota) {
    const viagem = page.getByRole('region', { name: /^Viagem \d+$/ }).filter({ has: page.getByLabel('Entregador', { exact: true }) }).last()
    if (await viagem.getByLabel('Entregador', { exact: true }).inputValue() !== prova.entregadorId) await agir(`/api/atendimento/viagens/${prova.viagemId}/entregador`, 'PUT', () => viagem.getByLabel('Entregador', { exact: true }).selectOption(prova.entregadorId))
    if (!prova.parada) { await agir(`/api/atendimento/viagens/${prova.viagemId}/paradas`, 'POST', () => cartaoDia().getByRole('button', { name: /^Pôr na viagem / }).last().click()); prova.parada = true; salvar() }
    assert.equal(await viagem.getByLabel('Entregador', { exact: true }).inputValue(), prova.entregadorId)
    await page.screenshot({ path: resolve(pasta, 'responsavel-viagem.png'), fullPage: true })
    await agir(`/api/atendimento/viagens/${prova.viagemId}/sair`, 'POST', () => viagem.getByRole('button', { name: 'Saiu para entrega', exact: true }).click())
    prova.emRota = true; salvar()
  }
  etapa = 'entrega-com-recebimento'
  if (!prova.entregue) {
    const viagem = page.getByRole('region', { name: /^Viagem \d+$/ }).filter({ hasText: 'Responsável fictício R4' }).last()
    await viagem.getByRole('button', { name: 'Marcar entregue', exact: true }).click(); await viagem.getByLabel('Como recebeu', { exact: true }).selectOption('dinheiro')
    await agir(`/api/atendimento/viagens/${prova.viagemId}/paradas/${prova.pedidoId}/entregue`, 'POST', () => viagem.getByRole('button', { name: /^Recebi R\$/ }).click())
    prova.entregue = true; salvar()
  }
  await page.reload(); prova.final = await detalhe(); prova.viagens = await ler('/api/atendimento/viagens'); prova.saldoFinal = await saldo()
  assert.equal(prova.final.pedido.status, 'entregue'); assert.equal(prova.final.pedido.totalPago, prova.total); assert.equal(prova.final.pagamentos.length, 1); assert.equal(prova.saldoFinal, prova.saldoInicial - 1)
  const v = prova.viagens.find(v => v.id === prova.viagemId); assert.equal(v.entregadorId, prova.entregadorId); assert.ok(v.paradas.find(p => p.pedidoId === prova.pedidoId).entregueEm)
  registrar('J04 Entregas: responsável atribuído, viagem despachada, recebimento na porta e entrega confirmados após reload', { pedidoId: prova.pedidoId, viagemId: prova.viagemId, entregadorId: prova.entregadorId })
  etapa = 'refluxo-teclado'
  prova.viewports = []
  for (const width of [320, 768]) {
    await page.setViewportSize({ width, height: width === 320 ? 844 : 900 }); await page.reload()
    await page.getByRole('radio', { name: 'Amanhã', exact: true }).click()
    await page.getByRole('region', { name: /Janela.*Tarde/ }).waitFor()
    const tab = page.getByRole('tab', { name: 'Painel do dia', exact: true }); await tab.waitFor(); await tab.focus(); await page.keyboard.press('ArrowRight')
    await page.getByRole('tab', { name: 'Entregadores', exact: true }).waitFor()
    const estado = await page.evaluate(() => ({ largura: innerWidth, scroll: document.documentElement.scrollWidth, foco: document.activeElement?.textContent, selecionado: document.activeElement?.getAttribute('aria-selected') }))
    assert.ok(estado.scroll <= width); assert.equal(estado.foco, 'Entregadores'); assert.equal(estado.selecionado, 'true')
    await page.keyboard.press('ArrowLeft'); await page.getByRole('region', { name: /Janela.*Tarde/ }).waitFor()
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    await page.screenshot({ path: resolve(pasta, `entregas-${width}-teclado.png`), fullPage: true }); prova.viewports.push(estado)
  }
  registrar('U02 complemento320/768 sem overflow global; setas trocam aba e foco acessível', { viewports: prova.viewports })
  assert.deepEqual(erros, []); delete prova.falha
} catch (e) { prova.falha = { etapa, mensagem: e.message }; await page.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); writeFileSync(resolve(pasta, 'falha.txt'), (await page.locator('body').innerText()).slice(0, 18000)); throw e }
finally { prova.erros = erros; prova.escopo = 'UI/JWT/API/PostgreSQL reais; somente provedores controlados. Sem teclado virtual físico, zoom ou orientação.'; salvar(); await browser.close() }
