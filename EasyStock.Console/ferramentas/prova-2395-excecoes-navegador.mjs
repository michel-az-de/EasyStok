// Exceções pelos controles reais; fetch direto é exclusivamente GET para reconciliação.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
assert.equal(process.env.R4_CONFERENCIA_LIBERADA, '1', 'Executar somente depois de root concluir C02 nesta conversa.')
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const fixture = JSON.parse(readFileSync(process.env.R4_FIXTURE ?? '../.build/r4-browser.json', 'utf8'))
const origem = process.env.CONSOLE_TEST_URL
assert.ok(origem && fixture.conversaConferenciaId && fixture.produtoExcecao, 'Fixture deve fornecer conversaConferenciaId e produtoExcecao exclusivos.')
const conversaId = fixture.conversaConferenciaId, produto = fixture.produtoExcecao
const nomeProduto = new RegExp(produto.nome.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'i')
const pasta = resolve(import.meta.dirname, '../.build/2395/real/excecoes'); mkdirSync(pasta, { recursive: true })
const arquivo = resolve(pasta, 'resultado.json')
const prova = process.env.R4_CONTINUAR === '1' && existsSync(arquivo) ? JSON.parse(readFileSync(arquivo, 'utf8')) : { conversaId, resultados: [] }
assert.equal(prova.conversaId, conversaId)
const salvar = () => writeFileSync(arquivo, JSON.stringify(prova, null, 2))
const registrar = (caso, dados = {}) => { prova.resultados.push({ caso, ...dados }); salvar(); console.log(`ok ${caso}`) }
const browser = await chromium.launch({ headless: true }); let context, page, etapa = 'login'
const erros = []
async function login(perfil) {
  await context?.close()
  context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'pt-BR', reducedMotion: 'reduce' })
  page = await context.newPage(); page.setDefaultTimeout(25000); page.on('pageerror', e => erros.push(e.message))
  const pessoa = fixture.identidades.find(i => i.perfil === perfil && i.empresaId === fixture.empresaId)
  assert.ok(pessoa, `Perfil ${perfil} ausente`)
  await page.goto(origem + '/#/m/atendimento/balcao')
  await page.getByLabel('E-mail', { exact: true }).fill(pessoa.email)
  await page.getByLabel('Senha', { exact: true }).fill(pessoa.senha)
  await page.getByRole('button', { name: 'Entrar', exact: true }).click()
  await selecionar()
}
async function ler(caminho) {
  return page.evaluate(async caminho => {
    const sessao = JSON.parse(sessionStorage.getItem('easystok.sessao') ?? localStorage.getItem('easystok.sessao'))
    const r = await fetch(caminho, { headers: { Authorization: `Bearer ${sessao.token}` } })
    const texto = await r.text(); if (!r.ok) throw new Error(`GET ${caminho}: ${r.status} ${texto}`)
    return texto ? JSON.parse(texto).data : null
  }, caminho)
}
const saldo = () => ler(`/api/estoque/${produto.itemEstoqueId}?empresaId=${fixture.empresaId}`).then(x => x.qty)
const detalhe = () => ler(`/api/pedidos/${prova.pedidoId}?empresaId=${fixture.empresaId}`)
const vagas = () => ler(`/api/atendimento/comanda/janelas?itens=${produto.cardapioItemId}&dataInicio=${fixture.dataEntrega}&dataFim=${fixture.dataEntrega}`)
async function agir(caminho, metodo, gesto) {
  const resposta = page.waitForResponse(r => new URL(r.url()).pathname === caminho && r.request().method() === metodo)
  await gesto(); const r = await resposta; const texto = await r.text()
  assert.ok(r.ok(), `${metodo} ${caminho}: ${r.status()} ${texto}`)
  return texto ? JSON.parse(texto).data : null
}
async function selecionar() {
  await page.getByRole('button', { name: /^Todas\b/ }).click()
  const inbox = await ler('/api/atendimento/conversas?limite=100&pagina=1')
  const numero = inbox.find(c => c.id === conversaId)?.numeroComanda
  assert.ok(numero, 'Conversa reservada identificada pelo ID da fixture')
  await page.getByPlaceholder('Buscar conversa', { exact: true }).fill(`comanda ${numero}`)
  await page.getByRole('button', { name: new RegExp(`Comanda ${numero},`) }).click()
}
async function navegar(href) {
  let link = page.locator(`a[href="${href}"]:visible`).first()
  if (!await link.count()) { const menu = page.getByRole('button', { name: /^Menu de / }); if (await menu.count()) await menu.click() }
  link = page.locator(`a[href="${href}"]:visible`).first()
  if (!await link.count()) { await page.locator('a[href="#/"]:visible').first().click(); link = page.locator(`a[href="${href}"]:visible`).first() }
  await link.click()
}
async function atendimento() { await navegar('#/m/atendimento'); await selecionar() }
async function fecharPaineis() { for (const nome of [/^Fechar cardápio$/, /^Recolher Cardápio/, /^Recolher Comanda/]) { const b = page.getByRole('button', { name: nome }); if (await b.isVisible()) await b.click() } }
async function posVenda() { await page.getByRole('button', { name: /^Pós-venda\b/ }).first().click(); return page.getByRole('dialog').last() }
async function secao(modal, nome) { const b = modal.getByRole('button', { name: new RegExp(`^${nome}`) }).first(); if (await b.getAttribute('aria-expanded') === 'false') await b.click() }
async function cozinha() { await navegar('#/m/cozinha'); await page.getByRole('radio', { name: 'Amanhã', exact: true }).click() }
async function etapaPedido(nome, status) {
  const card = page.getByRole('listitem').filter({ hasText: nomeProduto }).filter({ has: page.getByRole('button', { name: 'Ver comanda', exact: true }) })
  await card.waitFor()
  assert.equal(await card.count(), 1)
  await agir(`/api/kds/pedidos/${prova.pedidoId}/status`, 'PATCH', () => card.getByRole('button', { name: nome, exact: true }).click())
  prova.status = status; salvar()
}
async function mensagem(texto) { await page.getByRole('textbox', { name: 'Mensagem para o cliente' }).fill(texto); await agir(`/api/atendimento/conversas/${conversaId}/mensagens`, 'POST', () => page.getByRole('button', { name: 'Enviar mensagem', exact: true }).click()) }
try {
  await login('Dona')
  if (!prova.pedidoId) {
    etapa = 'montagem-quantidade'
    assert.equal(await saldo(), 1)
    await page.getByRole('button', { name: 'Adicionar itens', exact: true }).click()
    const somar = page.getByRole('button', { name: new RegExp(`^(Somar )?${produto.nome}`, 'i') }).first()
    await somar.click(); await somar.click(); await fecharPaineis()
    await page.getByRole('button', { name: /^Revisar comanda/ }).first().click()
    const menos = page.getByRole('button', { name: 'Menos um', exact: true })
    await menos.click(); await menos.click()
    assert.equal(await page.getByRole('button', { name: 'Menos um', exact: true }).count(), 0, 'Zero retira item; não há quantidade negativa ou decimal nos controles inteiros')
    await fecharPaineis()
    const cobrarVazio = page.getByRole('button', { name: 'Gerar cobrança e enviar', exact: true })
    assert.ok(!await cobrarVazio.count() || !await cobrarVazio.isEnabled(), 'Comanda vazia não confirma pedido')
    await page.getByRole('button', { name: 'Adicionar itens', exact: true }).click(); await somar.click(); await somar.click(); await fecharPaineis()
    await page.getByRole('button', { name: /^Revisar comanda/ }).first().click()
    await page.getByLabel('Dia da entrega (opcional)', { exact: true }).fill(fixture.dataEntrega)
    await page.getByRole('button', { name: /Manhã.*vagas? livres?/ }).first().click(); await fecharPaineis()
    const avisos = page.getByRole('button', { name: 'Dispensar avisos do aparelho', exact: true }); if (await avisos.isVisible()) await avisos.click()
    await page.getByRole('button', { name: 'Gerar cobrança e enviar', exact: true }).click()
    await page.getByRole('radio', { name: 'Dinheiro na entrega', exact: true }).click()
    const pedido = await agir(`/api/atendimento/conversas/${conversaId}/pedido`, 'POST', () => page.getByRole('button', { name: 'Confirmar e enviar', exact: true }).click())
    prova.pedidoId = pedido.pedidoId; prova.total = pedido.total; prova.status = 'aguardando'; salvar()
    assert.equal(await saldo(), 1); registrar('R02 quantidade inteira: zero remove e impede pedido vazio; duas unidades persistidas', { pedidoId: prova.pedidoId })
  }
  etapa = 'atraso-reagendamento'
  if (!prova.reagendado) {
    await fecharPaineis(); await cozinha(); if (prova.status === 'aguardando') await etapaPedido('Começar preparo', 'preparando')
    await atendimento()
    if (!prova.atrasoRegistrado) {
      const modal = await posVenda(); await secao(modal, 'Ocorrências')
      await modal.getByRole('button', { name: 'Registrar ocorrência', exact: true }).click()
      await modal.getByLabel('Motivo da ocorrência', { exact: true }).selectOption('atraso')
      await modal.getByLabel('O que aconteceu?', { exact: true }).fill('Ensaio R4: atraso no preparo; cliente concordou em receber à tarde.')
      await agir('/api/ocorrencias', 'POST', () => modal.getByRole('button', { name: 'Salvar ocorrência', exact: true }).click())
      prova.atrasoRegistrado = true; salvar()
      await modal.getByRole('button', { name: 'Fechar janela', exact: true }).click()
    }
    prova.janelasAntes = await ler(`/api/pedidos/${prova.pedidoId}/janelas?dataInicio=${fixture.dataEntrega}&dataFim=${fixture.dataEntrega}`); salvar()
    await page.getByRole('button', { name: /^Revisar comanda/ }).first().click()
    const janela = page.getByRole('button', { name: /^Janela de entrega/ }).first(); if (await janela.count() && await janela.getAttribute('aria-expanded') === 'false') await janela.click()
    await page.getByLabel('Dia da nova entrega (opcional)', { exact: true }).fill(fixture.dataEntrega)
    const escolha = page.getByLabel('Nova janela de entrega', { exact: true })
    const valor = await escolha.locator('option').filter({ hasText: 'Tarde' }).getAttribute('value'); assert.ok(valor)
    await escolha.selectOption(valor)
    await context.setOffline(true); await page.getByRole('button', { name: 'Confirmar agendamento', exact: true }).click()
    await page.getByText(/Não foi possível confirmar a troca/).waitFor(); await context.setOffline(false)
    const antes = await ler(`/api/pedidos/${prova.pedidoId}/janelas`); assert.deepEqual(antes.atual, prova.janelasAntes.atual)
    await agir(`/api/pedidos/${prova.pedidoId}/janela`, 'PATCH', () => page.getByRole('button', { name: 'Confirmar agendamento', exact: true }).click())
    prova.janelasDepois = await ler(`/api/pedidos/${prova.pedidoId}/janelas?dataInicio=${fixture.dataEntrega}&dataFim=${fixture.dataEntrega}`)
    assert.notDeepEqual(prova.janelasDepois.atual, prova.janelasAntes.atual)
    const vaga = (lista, atual) => lista.find(j => j.janelaId === atual.janelaId && j.data === atual.data)
    assert.equal(vaga(prova.janelasDepois.janelas, prova.janelasAntes.atual).vagasRestantes, vaga(prova.janelasAntes.janelas, prova.janelasAntes.atual).vagasRestantes + 1)
    assert.equal(vaga(prova.janelasDepois.janelas, prova.janelasDepois.atual).vagasRestantes, vaga(prova.janelasAntes.janelas, prova.janelasDepois.atual).vagasRestantes - 1)
    prova.reagendado = true; salvar()
    await fecharPaineis(); await mensagem('Ensaio R4: reagendamento confirmado para a tarde, conforme combinado.'); await cozinha()
    await page.getByRole('listitem').filter({ hasText: nomeProduto }).filter({ has: page.getByRole('button', { name: 'Ver comanda', exact: true }) }).getByText(/13:00|13h|Tarde/).first().waitFor()
    registrar('E02 atraso registrado; falha preserva vaga anterior; troca confirmada e comunicada, Cozinha atualizada', { antes: prova.janelasAntes.atual, depois: prova.janelasDepois.atual })
  }
  etapa = 'falta-estoque'
  if (prova.status !== 'pronto' && !prova.cancelado) { await cozinha(); await etapaPedido('Marcar pronto', 'pronto') }
  if (!prova.faltaConferida) {
    assert.equal(await saldo(), 0)
    prova.desacertos = await ler('/api/estoque/desacertos'); assert.ok(prova.desacertos.some(d => d.produtoId === produto.produtoId))
    await atendimento(); await page.getByText(prova.desacertos.find(d => d.produtoId === produto.produtoId).texto, { exact: true }).waitFor()
    await page.screenshot({ path: resolve(pasta, 'falta-visivel.png'), fullPage: true })
    prova.faltaConferida = true; salvar(); registrar('E01 falta descoberta visível no Atendimento; saldo1→0 sem negativo, decisão gerencial registrada a seguir')
  }
  etapa = 'recebimento-valores'
  if (!prova.pago) {
    await page.getByRole('button', { name: /^Pagamento\b/ }).first().click(); const modal = page.getByRole('dialog').last()
    await modal.getByRole('button', { name: 'Marcar pago', exact: true }).click(); await modal.getByLabel('Como recebeu', { exact: true }).selectOption('dinheiro')
    const valor = modal.getByRole('textbox', { name: 'Valor recebido', exact: true }); prova.entradas = []
    for (const entrada of ['', '0', '-10', '100000']) { await valor.fill(entrada); const habilitado = await modal.getByRole('button', { name: 'Marcar pago', exact: true }).isEnabled(); prova.entradas.push({ entrada, exibida: await valor.inputValue(), habilitado }); assert.equal(habilitado, false) }
    await valor.fill(String(prova.total).replace('.', ','))
    await agir(`/api/pedidos/${prova.pedidoId}/pagamentos`, 'POST', () => modal.getByRole('button', { name: 'Marcar pago', exact: true }).click())
    prova.pago = true; salvar(); assert.equal((await detalhe()).pedido.totalPago, prova.total)
    await modal.getByRole('button', { name: 'Fechar janela', exact: true }).click(); registrar('R02 valores inválidos recusados; valor corrigido recebido e persistido', { entradas: prova.entradas })
  }
  etapa = 'atendente-solicita-gerente'
  if (!prova.solicitado) {
    await login('Atendimento'); await page.getByRole('button', { name: 'Ações da ficha', exact: true }).click()
    assert.equal(await page.getByRole('menuitem', { name: 'Cancelar pedido', exact: true }).count(), 0); await page.keyboard.press('Escape')
    const modal = await posVenda(); await secao(modal, 'Devoluções'); await modal.getByText('Só a dona ou um gerente pode confirmar a devolução.', { exact: true }).waitFor()
    await page.screenshot({ path: resolve(pasta, 'atendente-restrita.png'), fullPage: true }); await modal.getByRole('button', { name: 'Fechar janela', exact: true }).click()
    await page.getByRole('button', { name: 'Adicionar nota', exact: true }).click()
    const nota = page.getByRole('dialog', { name: /^Nota interna/ }); await nota.getByRole('textbox', { name: /^Texto da nota/ }).fill(`Ensaio R4 pedido ${prova.pedidoId}: falta de uma unidade e atraso; solicito à gerente cancelar e conferir devolução de ${prova.total}. Cliente informado.`)
    const gravada = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname.endsWith('/notas'))
    await nota.getByRole('button', { name: 'Fixar nota', exact: true }).click(); assert.ok((await gravada).ok())
    prova.solicitado = true; salvar(); registrar('E03 atendente vê limite, não cancela pago, registra solicitação contextual em nota interna')
  }
  etapa = 'gerente-cancela-devolve'
  await login('Dona')
  if (!prova.cancelado) {
    prova.vagasAntesCancelar = await vagas()
    await page.getByRole('button', { name: /^Notas\b/ }).first().click(); await page.locator('p:visible').filter({ hasText: new RegExp(`Ensaio R4 pedido ${prova.pedidoId}`) }).first().waitFor()
    await page.getByRole('button', { name: 'Ações da ficha', exact: true }).click(); await page.getByRole('menuitem', { name: 'Cancelar pedido', exact: true }).click()
    await page.getByLabel('Motivo do cancelamento', { exact: true }).fill('Ensaio R4: gerente conferiu falta de produto e atraso; cancelamento acordado com cliente, devolução separada.')
    await agir(`/api/pedidos/${prova.pedidoId}/cancelar`, 'POST', () => page.getByRole('button', { name: 'Cancelar pedido', exact: true }).click())
    prova.cancelado = true; salvar(); prova.aposCancelar = await detalhe(); assert.equal(prova.aposCancelar.pedido.totalPago, prova.total)
    assert.equal(await saldo(), 1, 'Cancelamento devolve só uma saída real e abate a unidade descoberta')
    assert.ok(!(await ler('/api/estoque/desacertos')).some(d => d.produtoId === produto.produtoId), 'Desacerto resolvido pelo cancelamento')
    prova.vagasDepoisCancelar = await vagas()
    const janelaLiberada = prova.janelasDepois.atual.janelaId
    assert.equal(prova.vagasDepoisCancelar.janelas.find(j => j.janelaId === janelaLiberada).vagasRestantes,
      prova.vagasAntesCancelar.janelas.find(j => j.janelaId === janelaLiberada).vagasRestantes + 1, 'Cancelamento libera exatamente a vaga ocupada')
    salvar()
  }
  if (!prova.devolvido) {
    const modal = await posVenda(); await secao(modal, 'Conferir acerto|Devoluções')
    await modal.getByRole('button', { name: 'Registrar devolução manual', exact: true }).click()
    await modal.getByLabel('Meio usado na devolução', { exact: true }).selectOption('dinheiro')
    await modal.getByLabel('Motivo da devolução', { exact: true }).fill('Ensaio R4: devolução total em dinheiro após cancelamento autorizado.')
    await modal.getByRole('checkbox', { name: 'Confirmo que já devolvi este valor ao cliente.', exact: true }).check()
    await agir(`/api/pedidos/${prova.pedidoId}/estornos-manuais`, 'POST', () => modal.getByRole('button', { name: 'Confirmar devolução já realizada', exact: true }).click())
    prova.devolvido = true; salvar(); await page.reload()
    prova.final = await detalhe(); prova.estornos = await ler(`/api/pedidos/${prova.pedidoId}/estornos-manuais`); prova.saldoFinal = await saldo()
    assert.equal(prova.final.pedido.totalPago, prova.total); assert.equal(prova.estornos.estornos.length, 1); assert.equal(prova.estornos.estornos[0].valor, prova.total); assert.equal(prova.saldoFinal, 1)
    registrar('E04 gerente cancela com motivo, recebimento preservado, devolução única separada e persistida após reload')
  }
  await login('Atendimento'); await page.getByRole('button', { name: 'Ações da ficha', exact: true }).click()
  assert.equal(await page.getByRole('menuitem', { name: 'Cancelar pedido', exact: true }).count(), 0); await page.keyboard.press('Escape')
  const assumir = page.getByRole('button', { name: 'Assumir conversa', exact: true })
  if (await assumir.isVisible()) {
    page.once('dialog', async dialogo => {
      assert.equal(dialogo.type(), 'confirm')
      assert.match(dialogo.message(), /Confirme para assumir/)
      await dialogo.accept()
    })
    await assumir.click()
    const confirmar = page.getByRole('dialog', { name: 'Assumir esta conversa?', exact: true })
    if (await confirmar.count()) await confirmar.getByRole('button', { name: 'Assumir conversa', exact: true }).click()
    await page.getByRole('button', { name: 'Assumir conversa', exact: true }).waitFor({ state: 'hidden' })
  }
  await mensagem('Ensaio R4: cancelamento e devolução conferidos pela gerente. Atendimento retomado.')
  await page.screenshot({ path: resolve(pasta, 'atendente-retoma.png'), fullPage: true }); registrar('E03 atendente retoma conversa após decisão sem ganhar privilégio gerencial')
  assert.deepEqual(erros, []); delete prova.falha
} catch (e) {
  prova.falha = { etapa, mensagem: e.message }
  if (page) { await page.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); writeFileSync(resolve(pasta, 'falha.txt'), (await page.locator('body').innerText()).slice(0, 16000)) }
  throw e
} finally { prova.erros = erros; prova.escopo = 'UI/API/JWT/PostgreSQL reais; provedores controlados. Política de estoque padrão permite descoberta sem saldo negativo. Nota interna registra solicitação, sem workflow formal de aprovação.'; salvar(); await browser.close() }
