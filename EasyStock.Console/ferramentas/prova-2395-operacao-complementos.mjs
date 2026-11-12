// Opt-in: controles reais, JWT/PG locais e provedores controlados. Escritas de negócio só pela UI.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'

assert.equal(process.env.R4_EXECUTAR_COMPLEMENTOS, '1', 'Fixture final deve estar liberada pelo coordenador.')
const origem = process.env.CONSOLE_TEST_URL
const caminhoFixture = process.env.R4_FIXTURE
const fixture = JSON.parse(readFileSync(caminhoFixture, 'utf8'))
for (const url of [origem, fixture.api]) assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(new URL(url).hostname), 'Somente loopback')
assert.match(JSON.stringify(fixture.provedores), /controlados/i)
assert.ok(fixture.conversaOperacionalId && fixture.conversaExcecoesId && fixture.produtoSemEstoque)
assert.ok(process.env.R4_BACKEND_REVISAO && /^[A-Fa-f0-9]{64}$/.test(process.env.R4_BACKEND_SHA256 ?? ''), 'Identificar o binário servido')
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const rodada = process.env.R4_RODADA_COMPLEMENTOS ?? ''
assert.match(rodada, /^[a-z0-9-]*$/)
const pasta = resolve(import.meta.dirname, `../.build/2395/real/operacao-complementos${rodada ? `-${rodada}` : ''}`)
mkdirSync(pasta, { recursive: true })
const arquivo = resolve(pasta, 'resultado.json')
const sha = v => createHash('sha256').update(v).digest('hex')
const prova = process.env.R4_CONTINUAR === '1' && existsSync(arquivo) ? JSON.parse(readFileSync(arquivo, 'utf8')) : {
  empresaId: fixture.empresaId, casos: [], pedidos: {}, versao: {
    head: execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim(),
    scriptSha256: sha(readFileSync(import.meta.filename)), fixtureSha256: sha(readFileSync(caminhoFixture)),
    api: process.env.R4_BACKEND_REVISAO, apiDllSha256: process.env.R4_BACKEND_SHA256,
  },
}
assert.equal(prova.empresaId, fixture.empresaId)
const salvar = () => writeFileSync(arquivo, JSON.stringify(prova, null, 2))
const feito = id => prova.casos.some(c => c.id === id)
const registrar = (id, dados = {}) => { prova.casos.push({ id, ...dados }); salvar(); console.log(`ok ${id}`) }
const browser = await chromium.launch({ headless: true })
let context, page, conversaId, etapa = 'login'
const erros = [], produto = fixture.produtos[0], empresa = fixture.empresaId
const rx = texto => new RegExp(texto.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
async function ler(caminho) {
  assert.ok(caminho.startsWith('/api/'))
  return page.evaluate(async caminho => {
    const s = JSON.parse(sessionStorage.getItem('easystok.sessao') ?? localStorage.getItem('easystok.sessao'))
    const r = await fetch(caminho, { headers: { Authorization: `Bearer ${s.token}` } })
    const texto = await r.text()
    if (!r.ok) throw new Error(`GET ${caminho}: ${r.status} ${texto}`)
    return texto ? JSON.parse(texto).data : null
  }, caminho)
}
const detalhe = id => ler(`/api/pedidos/${id}?empresaId=${empresa}`)
const mensagens = id => ler(`/api/atendimento/conversas/${id}/mensagens?limite=200`)
const estoque = p => ler(`/api/estoque/${p.itemEstoqueId}?empresaId=${empresa}`)
async function foto(nome) { await page.screenshot({ path: resolve(pasta, `${nome}.png`), fullPage: true }) }
async function login(perfil, id) {
  await context?.close()
  context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'pt-BR', reducedMotion: 'reduce' })
  page = await context.newPage(); page.setDefaultTimeout(25000); page.on('pageerror', e => erros.push(e.message))
  const pessoa = fixture.identidades.find(i => i.perfil === perfil && i.empresaId === empresa)
  assert.ok(pessoa, `Perfil real ${perfil}`)
  await page.goto(`${origem}/#/m/atendimento/balcao`)
  await page.getByLabel('E-mail', { exact: true }).fill(pessoa.email)
  await page.getByLabel('Senha', { exact: true }).fill(pessoa.senha)
  await page.getByRole('button', { name: 'Entrar', exact: true }).click()
  await selecionar(id)
}
async function selecionar(id) {
  conversaId = id
  await page.getByRole('button', { name: /^Todas\b/ }).click()
  const inbox = await ler('/api/atendimento/conversas?limite=100&pagina=1')
  const numero = inbox.find(c => c.id === id)?.numeroComanda
  assert.ok(numero)
  await page.getByPlaceholder('Buscar conversa', { exact: true }).fill(`comanda ${numero}`)
  await page.getByRole('button', { name: new RegExp(`Comanda ${numero},`) }).click()
}
async function navegar(href) {
  let link = page.locator(`a[href="${href}"]:visible`).first()
  if (!await link.count()) {
    const menu = page.getByRole('button', { name: /^Menu de / }); if (await menu.count()) await menu.click()
    link = page.locator(`a[href="${href}"]:visible`).first()
  }
  if (!await link.count()) { await page.locator('a[href="#/"]:visible').first().click(); link = page.locator(`a[href="${href}"]:visible`).first() }
  await link.click()
}
async function atendimento(id) { await navegar('#/m/atendimento'); await selecionar(id) }
async function fecharPaineis() {
  for (const nome of [/^Fechar cardápio$/, /^Recolher Cardápio/, /^Recolher Comanda/]) {
    const b = page.getByRole('button', { name: nome }); if (await b.isVisible()) await b.click()
  }
}
async function agir(caminho, metodo, gesto) {
  const esperando = page.waitForResponse(r => new URL(r.url()).pathname === caminho && r.request().method() === metodo)
  await gesto(); const r = await esperando, texto = await r.text()
  assert.ok(r.ok(), `${metodo} ${caminho}: ${r.status()} ${texto}`)
  return texto ? JSON.parse(texto).data : null
}
async function preparar() {
  await page.getByRole('button', { name: 'Adicionar itens', exact: true }).click()
  await page.getByRole('button', { name: new RegExp(`^(Somar )?${rx(produto.nome).source}`, 'i') }).first().click()
  await fecharPaineis(); await page.getByRole('button', { name: /^Revisar comanda/ }).first().click()
  await page.getByLabel('Dia da entrega (opcional)', { exact: true }).fill(fixture.dataEntrega)
  await page.getByRole('button', { name: /Manhã.*vagas? livres?/ }).first().click(); await fecharPaineis()
  const aviso = page.getByRole('button', { name: 'Dispensar avisos do aparelho', exact: true }); if (await aviso.isVisible()) await aviso.click()
  await page.getByRole('button', { name: 'Gerar cobrança e enviar', exact: true }).click()
  await page.getByRole('radio', { name: 'Dinheiro na entrega', exact: true }).click()
}
async function nota(texto) {
  await page.getByRole('button', { name: 'Adicionar nota', exact: true }).click()
  const modal = page.getByRole('dialog', { name: /^Nota interna/ })
  await modal.getByRole('textbox', { name: /^Texto da nota/ }).fill(texto)
  const resposta = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname.endsWith('/notas'))
  await modal.getByRole('button', { name: 'Fixar nota', exact: true }).click(); assert.ok((await resposta).ok())
}
async function posVenda() { await page.getByRole('button', { name: /^Pós-venda\b/ }).first().click(); return page.getByRole('dialog').last() }
async function secao(modal, nome) { const b = modal.getByRole('button', { name: new RegExp(`^${nome}`) }).first(); if (await b.getAttribute('aria-expanded') === 'false') await b.click() }
async function receber(id) {
  await page.getByRole('button', { name: /^Pagamento\b/ }).first().click()
  const modal = page.getByRole('dialog').last()
  await modal.getByRole('button', { name: 'Marcar pago', exact: true }).click()
  await modal.getByLabel('Como recebeu', { exact: true }).selectOption('dinheiro')
  await agir(`/api/pedidos/${id}/pagamentos`, 'POST', () => modal.getByRole('button', { name: 'Marcar pago', exact: true }).click())
  return modal
}
async function abrirAssistente() {
  const painel = page.getByRole('region', { name: 'Assistente', exact: true })
  if (!await painel.isVisible()) await page.getByRole('button', { name: 'Abrir assistente', exact: true }).click()
  return painel
}
async function assumir() {
  const botao = page.getByRole('button', { name: 'Assumir conversa', exact: true })
  if (!await botao.isVisible()) return
  const confirmarNativo = async d => { assert.equal(d.type(), 'confirm'); assert.match(d.message(), /Confirme para assumir/); await d.accept() }
  page.on('dialog', confirmarNativo)
  try {
    await botao.click()
    const modal = page.getByRole('dialog', { name: 'Assumir esta conversa?', exact: true })
    if (await modal.isVisible()) await modal.getByRole('button', { name: 'Assumir conversa', exact: true }).click()
    await botao.waitFor({ state: 'hidden' })
  } finally { page.off('dialog', confirmarNativo) }
}
try {
  await login('Dona', fixture.conversaOperacionalId)
  etapa = 'J02'
  if (!prova.pedidos.operacional) {
    assert.equal(await ler(`/api/atendimento/conversas/${conversaId}/pedido`), null)
    const saldoAntes = await estoque(produto), caminho = `/api/atendimento/conversas/${conversaId}/pedido`
    await preparar()
    let chegou, liberar, posts = 0
    const persistido = new Promise(r => { chegou = r }), retido = new Promise(r => { liberar = r })
    await page.route(`**${caminho}`, async rota => {
      if (rota.request().method() !== 'POST') return rota.continue()
      posts++
      const r = await rota.fetch(), corpo = await r.json(); assert.ok(r.ok())
      prova.pedidos.operacional = corpo.data.pedidoId; prova.confirmacao = { pedido: corpo.data, corpo: rota.request().postDataJSON(), posts }; salvar(); chegou()
      await retido; await rota.abort('failed').catch(() => {})
    })
    await page.getByRole('button', { name: 'Confirmar e enviar', exact: true }).dblclick()
    await persistido; assert.equal(posts, 1); await foto('J02-confirmacao-sem-resposta')
    const recarga = page.reload(); liberar(); await recarga; await page.unroute(`**${caminho}`); await selecionar(conversaId)
    const confirmado = await ler(`/api/atendimento/conversas/${conversaId}/pedido`)
    assert.equal(confirmado.pedidoId, prova.pedidos.operacional); assert.equal(posts, 1)
    assert.equal((await estoque(produto)).qty, saldoAntes.qty, 'Confirmar não baixa estoque')
    const cliente = await ler(`/api/clientes/${prova.confirmacao.corpo.clienteId}?empresaId=${empresa}`)
    assert.equal(cliente.orderCount, 1, 'Pessoa exclusiva tem um pedido após duplo gesto e reload')
    await foto('J02-reload-mesmo-pedido'); registrar('J02', { pedidoId: confirmado.pedidoId, posts, orderCount: cliente.orderCount, estoque: saldoAntes.qty })
  }
  etapa = 'J05'
  if (!feito('J05')) {
    const id = prova.pedidos.operacional
    const dAntes = await detalhe(id)
    let modal
    if (dAntes.pedido.totalPago === 0) modal = await receber(id)
    else { await page.getByRole('button', { name: /^Pagamento\b/ }).first().click(); modal = page.getByRole('dialog').last() }
    const d = await detalhe(id), pagamento = d.pagamentos[0]
    assert.equal(d.pedido.totalPago, d.pedido.total); assert.ok(pagamento.registradoPorNome)
    await modal.getByText(pagamento.registradoPorNome, { exact: false }).first().waitFor(); await foto('J05-extrato-autoria')
    await modal.getByRole('button', { name: 'Fechar janela', exact: true }).click()
    await navegar('#/m/financeiro/caixa')
    const caixa = await ler('/api/caixa/dia'), linha = caixa.linhasExtras.find(l => l.pedidoNumeroDoDia === d.pedido.numeroDoDia)
    assert.ok(linha); assert.equal(linha.valor, d.pedido.total); assert.equal(linha.registradoPorNome, pagamento.registradoPorNome)
    await page.getByText(pagamento.registradoPorNome, { exact: false }).first().waitFor(); await foto('J05-caixa-autoria')
    registrar('J05', { pedido: d.pedido, pagamento, linha, totaisDisponiveis: { vendas: caixa.totalVendas, recebido: caixa.totalPagamentosPedidos, entradasExtras: caixa.totalEntradasExtras, saidasExtras: caixa.totalSaidasExtras, saldoEsperado: caixa.saldoEsperado }, limite: 'Campos reais exibidos; nenhum custo ou taxa ausente foi estimado.' })
    const fechar = page.getByRole('button', { name: 'Fechar janela', exact: true }); if (await fechar.isVisible()) await fechar.click()
  }
  etapa = 'E01'
  await atendimento(fixture.conversaExcecoesId)
  if (!feito('E01')) {
    assert.equal((await estoque(fixture.produtoSemEstoque)).qty, 0)
    assert.equal(await ler(`/api/atendimento/conversas/${conversaId}/pedido`), null)
    const msgsAntes = await mensagens(conversaId)
    await page.getByRole('button', { name: 'Adicionar itens', exact: true }).click()
    await page.getByRole('button', { name: new RegExp(`^(Somar )?${rx(fixture.produtoSemEstoque.nome).source}`, 'i') }).first().click()
    await page.getByText(/Sem saldo, mas ainda vende\. No lugar dele:/).waitFor(); await foto('E01-falta-antes-promessa')
    await fecharPaineis(); await page.getByRole('button', { name: /^Revisar comanda/ }).first().click()
    await page.getByRole('button', { name: 'Menos um', exact: true }).click(); await fecharPaineis()
    await nota(`R4 E01: responsável Dona. ${fixture.produtoSemEstoque.nome} sem saldo; não prometer esse item. Decisão: retirar da proposta e oferecer ${produto.nome}, após conferência.`)
    assert.deepEqual(await mensagens(conversaId), msgsAntes); assert.equal(await ler(`/api/atendimento/conversas/${conversaId}/pedido`), null)
    registrar('E01', { produtoId: fixture.produtoSemEstoque.produtoId, saldo: 0, decisao: 'Dona retirou da proposta antes de confirmar; nenhum envio nem pedido até a decisão.', politica: 'Venda sem saldo permitida com aviso pelo produto existente, sem bloqueio inventado.' })
  }
  if (!prova.pedidos.excecoes) {
    await preparar()
    const p = await agir(`/api/atendimento/conversas/${conversaId}/pedido`, 'POST', () => page.getByRole('button', { name: 'Confirmar e enviar', exact: true }).click())
    prova.pedidos.excecoes = p.pedidoId; salvar()
  }
  etapa = 'E02'
  if (!feito('E02')) {
    const id = prova.pedidos.excecoes
    const numeroComanda = (await ler('/api/atendimento/conversas?limite=100&pagina=1')).find(c => c.id === conversaId).numeroComanda
    await navegar('#/m/cozinha'); await page.getByRole('radio', { name: 'Amanhã', exact: true }).click()
    const card = page.getByRole('listitem').filter({ hasText: new RegExp(`Comanda ${numeroComanda}(?=\\D|$)`) }).filter({ has: page.getByRole('button', { name: 'Ver comanda', exact: true }) })
    await card.getByRole('button', { name: 'Ver comanda', exact: true }).click()
    const modal = page.getByRole('dialog')
    await modal.getByRole('button', { name: 'Registrar atraso', exact: true }).click()
    const motivo = `R4 E02 origem Cozinha, comanda ${numeroComanda}, pedido ${id}: preparo atrasado; Atendimento deve conferir novo horário antes de prometer.`
    await modal.getByLabel('O que atrasou?', { exact: true }).fill(motivo)
    await foto('E02-comanda-na-cozinha')
    const ocorrencia = await agir('/api/ocorrencias', 'POST', () => modal.getByRole('button', { name: 'Salvar ocorrência', exact: true }).click())
    await modal.getByRole('status').filter({ hasText: 'Atraso registrado.' }).waitFor(); await foto('E02-atraso-registrado-na-origem')
    await modal.getByRole('button', { name: 'Fechar janela', exact: true }).click(); await atendimento(fixture.conversaExcecoesId); await page.reload(); await selecionar(conversaId)
    const inbox = await ler('/api/atendimento/conversas?limite=100&pagina=1'), c = inbox.find(c => c.id === conversaId)
    assert.equal(c.situacao, 'Assumida'); assert.equal(c.assumidaPorUsuarioId, null); assert.ok(c.motivoEscalada.includes(motivo))
    await page.getByText(rx(motivo)).first().waitFor(); await foto('E02-origem-cozinha-fila')
    registrar('E02', { ocorrencia, fila: c, origemUI: 'Dona na Cozinha → Ver comanda → Registrar atraso → confirmação API → fila Atendimento da mesma conversa.', perfil: 'Dona, com capability gerenciarOcorrencias; perfil Cozinha não recebe privilégio gerencial.' })
  }
  etapa = 'A01'
  if (!feito('A01')) {
    await assumir()
    const id = prova.pedidos.excecoes, antes = await detalhe(id), msgs = await mensagens(conversaId)
    const campo = page.getByRole('textbox', { name: 'Mensagem para o cliente', exact: true }), rascunho = await campo.inputValue()
    const caminho = `/api/atendimento/conversas/${conversaId}/sugestao`, painel = await abrirAssistente()
    const sugestao = await agir(caminho, 'POST', () => painel.getByRole('button', { name: 'Sugerir resposta', exact: true }).click())
    await painel.getByRole('button', { name: 'Descartar', exact: true }).waitFor(); assert.match(sugestao.texto, /Confira os dados atuais da sua comanda:/)
    const pedidoConsultado = JSON.parse(sugestao.texto.slice(sugestao.texto.indexOf('{')))
    const reais = valor => Number(String(valor).replace(/[^\d,.-]/g, '').replace(/\./g, '').replace(',', '.'))
    assert.equal(pedidoConsultado.pedidoId, id)
    assert.equal(pedidoConsultado.status, antes.pedido.status)
    assert.equal(reais(pedidoConsultado.total), antes.pedido.total)
    assert.equal(reais(pedidoConsultado.pago), antes.pedido.totalPago)
    assert.equal(reais(pedidoConsultado.faltaPagar), antes.pedido.total - antes.pedido.totalPago)
    assert.ok(reais(pedidoConsultado.faltaPagar) > 0, 'Impedimento financeiro preparado e confirmado pela ferramenta real')
    assert.ok(pedidoConsultado.itens.some(i => i.nome === produto.nome && i.quantidade === 1))
    assert.deepEqual(await detalhe(id), antes); assert.deepEqual(await mensagens(conversaId), msgs); assert.equal(await campo.inputValue(), rascunho)
    await foto('A01-previa-sem-efeitos'); await painel.getByRole('button', { name: 'Descartar', exact: true }).click()
    await page.route(`**${caminho}`, rota => rota.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ error: { code: 'AGENTE_INDISPONIVEL', message: 'Assistente indisponível na prova controlada.', details: null, traceId: null } }) }))
    await painel.getByRole('button', { name: 'Sugerir resposta', exact: true }).click(); await painel.getByRole('alert').waitFor(); await foto('A01-503-fallback')
    await page.unroute(`**${caminho}`)
    let chegou, liberar, terminou
    const respostaPronta = new Promise(r => { chegou = r }), retida = new Promise(r => { liberar = r }), respostaEncerrada = new Promise(r => { terminou = r })
    await page.route(`**${caminho}`, async rota => { const resposta = await rota.fetch(); chegou(); await retida; await rota.fulfill({ response: resposta }).catch(() => {}); terminou() })
    await painel.getByRole('button', { name: 'Tentar novamente', exact: true }).click(); await respostaPronta
    await selecionar(fixture.conversaOperacionalId); liberar(); await respostaEncerrada; await page.unroute(`**${caminho}`)
    assert.equal(await page.getByRole('button', { name: 'Usar no rascunho', exact: true }).count(), 0, 'Resposta antiga não oferece aplicação na outra conversa')
    await selecionar(fixture.conversaExcecoesId)
    await campo.fill('Conferi manualmente o atraso. Vou confirmar o novo horário antes de prometer a entrega.')
    assert.deepEqual(await detalhe(id), antes); assert.deepEqual(await mensagens(conversaId), msgs); await foto('A01-manual-sem-envio')
    registrar('A01', { sugestao, pedidoConsultado, pedidoId: id, impedimento: 'Saldo financeiro pendente real; consultar_pedido não expõe a ocorrência de atraso e a prova não lhe atribui essa informação.', controles: ['prévia', 'descarte', '503 controlado no código real', 'resposta real retida até trocar conversa', 'rascunho manual'], efeitos: 'Pedido e mensagens iguais antes/depois; sem inferência externa, LLM controlado chama consultar_pedido real.' })
  }
  etapa = 'E03'
  if (!feito('E03')) {
    const id = prova.pedidos.excecoes
    if ((await detalhe(id)).pedido.totalPago === 0) { const m = await receber(id); await m.getByRole('button', { name: 'Fechar janela', exact: true }).click() }
    const antes = await detalhe(id)
    await login('Atendimento', fixture.conversaExcecoesId)
    await page.getByRole('button', { name: 'Ações da ficha', exact: true }).click(); assert.equal(await page.getByRole('menuitem', { name: 'Cancelar pedido', exact: true }).count(), 0); await page.keyboard.press('Escape')
    await nota(`R4 E03 pedido ${id}: solicito decisão da gerente sobre cancelar pedido pago por atraso. Aguardar avaliação antes de prometer devolução.`)
    await login('Dona', fixture.conversaExcecoesId)
    const recusa = `R4 E03 pedido ${id}: recuso cancelamento imediato; confirmar com cliente o novo horário. Próximo passo: Atendimento consultar disponibilidade e retornar antes de alterar pedido.`
    await nota(recusa); assert.deepEqual(await detalhe(id), antes)
    await login('Atendimento', fixture.conversaExcecoesId)
    await page.getByRole('button', { name: /^Notas\b/ }).first().click(); await page.getByText(recusa, { exact: true }).first().waitFor(); await foto('E03-recusa-proximo-passo')
    await assumir()
    await page.getByRole('textbox', { name: 'Mensagem para o cliente', exact: true }).fill('Vou conferir o novo horário com você antes de alterar o pedido. A gerente pediu essa confirmação.')
    assert.deepEqual(await detalhe(id), antes)
    registrar('E03', { pedidoId: id, recusa, limite: 'Decisão e próximo passo em nota contextual; sem alegar workflow formal de aprovação.' })
  }
  etapa = 'E04'
  if (!feito('E04')) {
    await login('Dona', fixture.conversaExcecoesId)
    const id = prova.pedidos.excecoes, antes = await detalhe(id)
    if (antes.pedido.status !== 'cancelado') {
      await page.getByRole('button', { name: 'Ações da ficha', exact: true }).click(); await page.getByRole('menuitem', { name: 'Cancelar pedido', exact: true }).click()
      await page.getByLabel('Motivo do cancelamento', { exact: true }).fill('R4 E04: após recusa inicial, cliente recusou novo horário; Dona autoriza cancelamento e devolução separados.')
      await agir(`/api/pedidos/${id}/cancelar`, 'POST', () => page.getByRole('button', { name: 'Cancelar pedido', exact: true }).click())
    }
    const modal = await posVenda(); await secao(modal, 'Conferir acerto|Devoluções')
    await modal.getByRole('button', { name: 'Registrar devolução manual', exact: true }).click()
    await modal.getByLabel('Meio usado na devolução', { exact: true }).selectOption('dinheiro')
    await modal.getByLabel('Motivo da devolução', { exact: true }).fill('R4 E04 devolução fictícia controlada após cancelamento autorizado.')
    await modal.getByRole('checkbox', { name: 'Confirmo que já devolvi este valor ao cliente.', exact: true }).check()
    const caminho = `/api/pedidos/${id}/estornos-manuais`, posts = []
    await page.route(`**${caminho}`, async rota => {
      if (rota.request().method() !== 'POST') return rota.continue()
      posts.push(rota.request().postDataJSON()); const r = await rota.fetch(); assert.ok(r.ok())
      if (posts.length === 1) await rota.abort('failed'); else await rota.fulfill({ response: r })
    })
    await modal.getByRole('button', { name: 'Confirmar devolução já realizada', exact: true }).click()
    await modal.getByRole('button', { name: 'Repetir confirmação pendente', exact: true }).waitFor(); await foto('E04-resposta-perdida')
    await agir(caminho, 'POST', () => modal.getByRole('button', { name: 'Repetir confirmação pendente', exact: true }).click()); await page.unroute(`**${caminho}`)
    assert.equal(posts.length, 2); assert.deepEqual(posts[0], posts[1]); await page.reload(); await selecionar(conversaId)
    const estornos = await ler(caminho), depois = await detalhe(id)
    assert.equal(estornos.estornos.length, 1); assert.equal(estornos.estornos[0].valor, antes.pedido.totalPago); assert.deepEqual(depois.pagamentos, antes.pagamentos)
    await navegar('#/m/financeiro/caixa'); const caixa = await ler('/api/caixa/dia')
    const devolucoes = caixa.movimentos.filter(m => m.origem === 'devolucao_pedido' && m.descricao?.includes('R4 E04'))
    assert.equal(devolucoes.length, 1); assert.equal(devolucoes[0].valor, antes.pedido.totalPago)
    await page.getByText(/R4 E04 devolução fictícia controlada/).first().waitFor(); await foto('E04-caixa-devolucao-unica')
    registrar('E04', { pedidoId: id, posts, estornos, pagamentoPreservado: depois.pagamentos, devolucoes })
  }
  prova.sucesso = true; prova.erros = erros; delete prova.falha; salvar(); assert.deepEqual(erros, [])
} catch (erro) {
  prova.falha = { etapa, mensagem: erro.stack }; prova.erros = erros; salvar()
  if (page) { await foto(`falha-${etapa}`); writeFileSync(resolve(pasta, `falha-${etapa}.txt`), await page.locator('body').innerText()) }
  throw erro
} finally { await browser.close() }
