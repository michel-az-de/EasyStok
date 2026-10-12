// Console real, contratos HTTP controlados e nenhuma chamada à loja.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5198'
const pasta = process.env.EVIDENCIAS_UX ?? '../.build/1653/depois'
mkdirSync(pasta, { recursive: true })
const empresa = '11111111-1111-4111-8111-111111111111'
const usuario = '22222222-2222-4222-8222-222222222222'
const modulos = ['cardapio', 'producao', 'atendimento', 'cozinha', 'financeiro', 'campanhas', 'configuracoes', 'entregas']
const configuracaoInicial = {
  empresaId: empresa, ativo: true, tom: 'acolhedor, direto, sem gíria', nivelSugestao: 'Discreto',
  saudacaoPrimeiroContato: 'Olá! Veja nosso cardápio: {link}', saudacaoRetorno: 'Olá, {nome}! Veja: {link}',
  fraseEspera: 'Só um instante que já te ajudo!', mensagemForaArea: 'Por enquanto entregamos em algumas regiões.',
  respiroMinutos: 40, tempoPreparoPadraoMinutos: 60, slaRespostaMinutos: 5,
  modeloRetomadaNome: null, modeloRetomadaIdioma: 'pt_BR', webhookVerificadoEm: null, ultimaMensagemRecebidaEm: null,
}
const browser = await chromium.launch({ headless: true })
const resultados = []
let ultimaPagina
const conferir = async (nome, teste) => { await teste(); resultados.push(nome); console.log('ok ' + nome) }
async function preparar({ permitidos = modulos, editar = true } = {}) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 960 }, reducedMotion: 'reduce', hasTouch: true })
  const estado = {
    configuracao: { ...configuracaoInicial }, falhar: false, aguardar: null, aguardarHorarios: null,
    expediente: { controleManual: 'Automatico', horarios: [{ diaDaSemana: 1, abre: '08:00', fecha: '18:00' }], mensagemForaDoHorario: 'Voltamos em nosso horário.', mensagemLojaFechada: 'A loja está fechada.' },
    escritas: [], leituras: new Set(), erros: [],
  }
  await context.addInitScript(({ empresa, usuario }) => {
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova-local', expiraEm: Date.now() + 3600000, empresa: { id: empresa }, usuario: { id: usuario, nome: 'Felipe' } }))
    localStorage.setItem(`easystok.hall.v1:${empresa}:${usuario}`, JSON.stringify({ compacto: true }))
  }, { empresa, usuario })
  await context.route('**/api/**', async (route) => {
    const request = route.request(), path = new URL(request.url()).pathname, metodo = request.method()
    if (!path.startsWith('/api/')) return route.continue()
    const ok = (data) => route.fulfill({ json: { data } })
    if (metodo !== 'GET') {
      estado.escritas.push({ path, metodo, corpo: request.postDataJSON() })
      if (path === '/api/atendimento/configuracao' && metodo === 'PUT') {
        const corpo = request.postDataJSON()
        if (estado.aguardar) await estado.aguardar
        if (estado.falhar) return route.fulfill({ status: 503, json: { error: { message: 'Falha controlada. Tente novamente.' } } })
        estado.configuracao = { ...estado.configuracao, ...corpo }
        return ok(estado.configuracao)
      }
      if (path === '/api/atendimento/expediente' && metodo === 'PUT') {
        if (estado.aguardarHorarios) await estado.aguardarHorarios
        if (estado.falhar) return route.fulfill({ status: 503, json: { error: { message: 'Falha controlada. Tente novamente.' } } })
        estado.expediente = { ...estado.expediente, ...request.postDataJSON() }
        return ok(estado.expediente)
      }
      throw new Error(`Escrita inesperada: ${metodo} ${path}`)
    }
    estado.leituras.add(path)
    if (path === '/api/auth/me/modulos') return ok({ modulos: modulos.map(id => ({ id, liberado: permitidos.includes(id) })), acoes: { editarAtendimento: editar, controlarLoja: editar } })
    if (path === '/api/atendimento/configuracao') return ok(estado.configuracao)
    if (path === '/api/atendimento/expediente') return ok(estado.expediente)
    if (path === '/api/caixa/dia') return ok({ aberto: true, fechado: false, saldoEsperado: 300.03 })
    if (path === '/api/auth/google/config') return ok({ habilitado: false })
    if (path === '/api/notificacoes/badge') return ok({ count: 0 })
    return ok([])
  })
  const page = await context.newPage()
  page.setDefaultTimeout(10000)
  page.setDefaultNavigationTimeout(10000)
  ultimaPagina = page
  page.on('pageerror', erro => { estado.erros.push(erro.message); console.error(erro.message) })
  await page.goto(origem + '/#/')
  await page.getByLabel('Buscar módulo ou tela').waitFor()
  return { context, page, estado }
}
async function esperarValor(campo, valor) {
  await campo.evaluate((elemento, esperado) => new Promise((resolve, reject) => {
    const inicio = Date.now()
    const conferir = () => elemento.value === esperado ? resolve() : Date.now() - inicio > 5000 ? reject(new Error(`Valor recebido: ${elemento.value}`)) : setTimeout(conferir, 30)
    conferir()
  }), valor)
}
async function ajustes(page) {
  await page.evaluate(() => { window.location.hash = '#/m/atendimento/horarios' })
  await page.getByRole('button', { name: 'Salvar configuração', exact: true }).waitFor()
  await page.getByLabel('Fora do horário', { exact: true }).waitFor({ state: 'visible' })
}
async function foto(page, nome) {
  await page.evaluate(async () => { await document.fonts.ready })
  await page.screenshot({ path: join(pasta, nome + '.png'), fullPage: true })
}
try {
  const { page, context, estado } = await preparar()
  await conferir('busca compacta abre a tela exata sem organizar ou favoritar', async () => {
    await page.getByLabel('Buscar módulo ou tela').fill('impressao')
    const resultado = page.getByRole('region', { name: 'Resultados da busca' })
    assert.equal(await resultado.getByRole('link').count(), 1)
    assert.equal(await resultado.getByRole('link').getAttribute('href'), '#/m/configuracoes/impressao')
    assert.equal(await page.getByRole('button', { name: 'Organizar módulos' }).count(), 0)
    await foto(page, 'busca-impressao-desktop')
    await resultado.getByRole('link').click()
    await page.getByRole('heading', { name: 'Impressão', exact: true }).waitFor()
    assert.ok((await page.locator('[data-inicio-tela]').evaluate(el => el === document.activeElement)))
  })
  await conferir('menu mostra contexto no celular e avisa os recursos em preparação', async () => {
    await page.setViewportSize({ width: 320, height: 740 })
    const menu = page.getByRole('button', { name: 'Menu de Configurações' })
    assert.match(await menu.innerText(), /Configurações/)
    await menu.click()
    await page.getByRole('dialog').getByRole('link', { name: /Integrações de entrega.*Em preparação/ }).waitFor()
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    await foto(page, 'menu-celular-320')
    await page.keyboard.press('Escape')
    assert.equal(await menu.evaluate(el => el === document.activeElement), true)
    await page.setViewportSize({ width: 1440, height: 960 })
  })
  await ajustes(page)
  const fora = page.getByLabel('Fora do horário', { exact: true })
  await conferir('rascunho das mensagens sobrevive à ida ao início e volta', async () => {
    await fora.fill('Rascunho para um dia difícil.')
    await page.getByRole('link', { name: 'Módulos', exact: true }).click()
    await ajustes(page)
    await esperarValor(fora, 'Rascunho para um dia difícil.')
    const storage = await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))
    assert.ok(!storage.includes('Rascunho para um dia difícil.'))
  })
  await conferir('sair pede decisão e cancelar preserva a edição', async () => {
    await page.getByRole('banner').getByRole('button', { name: 'Sair', exact: true }).click()
    const dialogo = page.getByRole('dialog', { name: 'Sair sem salvar os ajustes?' })
    await dialogo.getByRole('button', { name: 'Continuar trabalhando' }).click()
    assert.equal(await fora.inputValue(), 'Rascunho para um dia difícil.')
    assert.equal(await page.getByRole('dialog').count(), 0)
  })
  await conferir('recarregar com rascunho pede confirmação do navegador', async () => {
    const dialogo = page.waitForEvent('dialog')
    const recarga = page.reload().catch(() => {})
    const aviso = await dialogo
    assert.equal(aviso.type(), 'beforeunload')
    await aviso.dismiss()
    await recarga
    assert.equal(await fora.inputValue(), 'Rascunho para um dia difícil.')
  })
  await conferir('falha ao salvar conserva mensagem, reenvio confirmado limpa pendência', async () => {
    estado.falhar = true
    await page.getByRole('button', { name: 'Salvar mensagens', exact: true }).click()
    await page.getByRole('alert').filter({ hasText: 'Falha ao salvar' }).waitFor()
    assert.equal(await fora.inputValue(), 'Rascunho para um dia difícil.')
    estado.falhar = false
    await page.getByRole('button', { name: 'Salvar mensagens', exact: true }).click()
    await page.getByText('Salvo no EasyStok.', { exact: true }).waitFor()
    assert.equal(await page.getByRole('button', { name: 'Salvar mensagens', exact: true }).isDisabled(), true)
  })
  await conferir('horários abrem aqui e fecham devolvendo o foco', async () => {
    const abrir = page.getByRole('button', { name: 'Ajustar horários da semana' })
    await abrir.click()
    await page.getByRole('dialog').waitFor()
    await page.getByRole('dialog', { name: 'Horários da semana' }).getByRole('button', { name: 'Salvar horários', exact: true }).waitFor()
    await page.keyboard.press('Escape')
    assert.equal(await abrir.evaluate(el => el === document.activeElement), true)
  })
  const config = page.getByRole('form', { name: 'Como o automático atende' })
  const tom = page.getByLabel('Tom', { exact: true })
  await conferir('horários preservam edição, exigem preenchimento e só confirmam após salvar', async () => {
    const antes = estado.escritas.length
    await page.getByRole('button', { name: 'Ajustar horários da semana' }).click()
    const dialogo = page.getByRole('dialog', { name: 'Horários da semana' })
    const segunda = dialogo.getByLabel('Abre, Segunda', { exact: true })
    await segunda.fill('09:30')
    await dialogo.getByRole('button', { name: 'Fechar', exact: true }).click()
    await page.getByRole('link', { name: 'Módulos', exact: true }).click()
    await page.getByLabel('Buscar módulo ou tela').waitFor()
    await ajustes(page)
    await page.getByRole('button', { name: 'Ajustar horários da semana' }).click()
    await esperarValor(segunda, '09:30')
    assert.equal(estado.escritas.length, antes, 'Navegação não salva silenciosamente')
    await segunda.fill('')
    await dialogo.getByRole('button', { name: 'Salvar horários', exact: true }).click()
    assert.equal(await segunda.evaluate(el => el.validity.valueMissing), true)
    assert.equal(estado.escritas.length, antes)
    await segunda.fill('09:30')
    estado.falhar = true
    await dialogo.getByRole('button', { name: 'Salvar horários', exact: true }).click()
    await dialogo.getByRole('alert').waitFor()
    await esperarValor(segunda, '09:30')
    estado.falhar = false
    await dialogo.getByRole('button', { name: 'Salvar horários', exact: true }).click()
    await dialogo.getByText('Horários salvos no EasyStok.', { exact: true }).waitFor()
    assert.equal(estado.expediente.horarios.find(dia => dia.diaDaSemana === 1).abre, '09:30')
    await foto(page, 'horarios-confirmados-desktop')
    await dialogo.getByRole('button', { name: 'Fechar', exact: true }).click()
  })
  await conferir('Voltar do navegador não permite sobrepor duas gravações da semana', async () => {
    await page.getByRole('button', { name: 'Ajustar horários da semana' }).click()
    const dialogo = page.getByRole('dialog', { name: 'Horários da semana' })
    const segunda = dialogo.getByLabel('Abre, Segunda', { exact: true })
    await segunda.fill('09:45')
    let liberar
    estado.aguardarHorarios = new Promise(resolve => { liberar = resolve })
    const resposta = page.waitForResponse(resposta => resposta.url().endsWith('/api/atendimento/expediente') && resposta.request().method() === 'PUT', { timeout: 30000 }).then(() => null, erro => erro)
    await dialogo.getByRole('button', { name: 'Salvar horários', exact: true }).click()
    await dialogo.getByRole('button', { name: 'Salvando…', exact: true }).waitFor()
    await page.goBack()
    await page.getByLabel('Buscar módulo ou tela').waitFor()
    await page.goForward()
    await ajustes(page)
    await page.getByRole('button', { name: 'Ajustar horários da semana' }).click()
    await segunda.fill('10:00')
    assert.equal(await dialogo.getByRole('button', { name: /Salvar horários|Salvando/ }).isDisabled(), true)
    liberar()
    estado.aguardarHorarios = null
    const erroResposta = await resposta
    if (erroResposta) throw erroResposta
    await esperarValor(segunda, '10:00')
    await dialogo.getByRole('button', { name: 'Salvar horários', exact: true }).click()
    await dialogo.getByText('Horários salvos no EasyStok.', { exact: true }).waitFor()
    assert.equal(estado.expediente.horarios.find(dia => dia.diaDaSemana === 1).abre, '10:00')
    await dialogo.getByRole('button', { name: 'Fechar', exact: true }).click()
  })
  await conferir('número vazio, mensagem só com espaços e modelo inválido não enviam PUT', async () => {
    const antes = estado.escritas.length
    await page.getByLabel('Preparo padrão (min)').fill('')
    await config.getByRole('button', { name: 'Salvar configuração', exact: true }).click()
    assert.equal(await page.getByLabel('Preparo padrão (min)').evaluate(el => el.validity.valueMissing), true)
    await page.getByLabel('Preparo padrão (min)').fill('60')
    await tom.fill('   ')
    await config.getByRole('button', { name: 'Salvar configuração', exact: true }).click()
    assert.equal(estado.escritas.length, antes)
    await tom.fill('acolhedor e claro')
    await page.getByLabel('Modelo de retomada (WhatsApp)').fill('Nome Inválido')
    await config.getByRole('button', { name: 'Salvar configuração', exact: true }).click()
    assert.equal(await page.getByLabel('Modelo de retomada (WhatsApp)').evaluate(el => el.validity.patternMismatch), true)
    assert.equal(estado.escritas.length, antes)
    await config.getByRole('button', { name: 'Desfazer edição da configuração' }).click()
  })
  await conferir('PUT atrasado não apaga edição mais nova depois de sair e voltar', async () => {
    await tom.fill('Primeira edição enviada')
    let liberar
    estado.aguardar = new Promise(resolve => { liberar = resolve })
    const respostaPendente = page.waitForResponse(resposta => resposta.url().endsWith('/api/atendimento/configuracao') && resposta.request().method() === 'PUT', { timeout: 30000 }).then(() => null, erro => erro)
    await config.getByRole('button', { name: 'Salvar configuração', exact: true }).click()
    await config.getByText('Salvando…', { exact: true }).waitFor()
    await page.getByRole('link', { name: 'Módulos', exact: true }).click()
    await page.getByLabel('Buscar módulo ou tela').waitFor()
    await ajustes(page)
    await tom.fill('Edição nova preservada')
    assert.equal(await config.getByRole('button', { name: /Salvar configuração|Salvando/ }).isDisabled(), true)
    liberar()
    estado.aguardar = null
    const erroResposta = await respostaPendente
    if (erroResposta) throw erroResposta
    await esperarValor(tom, 'Edição nova preservada')
    await page.getByRole('link', { name: 'Módulos', exact: true }).click()
    await page.getByLabel('Buscar módulo ou tela').waitFor()
    await ajustes(page)
    await esperarValor(tom, 'Edição nova preservada')
    await foto(page, 'configuracao-rascunho-desktop')
    await config.getByRole('button', { name: 'Desfazer edição da configuração' }).click()
    await esperarValor(tom, 'Primeira edição enviada')
  })
  await conferir('celular, tema escuro e fonte a 200% mantêm os controles acessíveis', async () => {
    await page.setViewportSize({ width: 390, height: 844 })
    await page.getByRole('radio', { name: 'Escuro', exact: true }).click()
    await page.getByRole('heading', { name: 'Horários e mensagens', exact: true }).scrollIntoViewIfNeeded()
    await foto(page, 'configuracoes-celular-escuro')
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%' })
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    for (const campo of await page.locator('input:not([type=checkbox]), textarea, select').all()) {
      if (!await campo.isVisible()) continue
      const caixa = await campo.boundingBox()
      assert.ok(caixa.width <= 390, 'Campo não deve exceder o celular')
    }
    await foto(page, 'configuracoes-fonte-200')
    await page.evaluate(() => { document.documentElement.style.fontSize = '' })
  })
  assert.deepEqual(estado.erros, [])
  await context.close()
  await conferir('perfil sem configuração não descobre a tela na busca nem ganha edição', async () => {
    const restrito = await preparar({ permitidos: ['atendimento'], editar: false })
    await restrito.page.getByLabel('Buscar módulo ou tela').fill('impressao')
    assert.equal(await restrito.page.getByRole('region', { name: 'Resultados da busca' }).getByRole('link').count(), 0)
    await restrito.page.evaluate(() => { window.location.hash = '#/m/atendimento/horarios' })
    await restrito.page.getByRole('heading', { name: 'Horários e mensagens', exact: true }).waitFor()
    assert.equal(await restrito.page.getByRole('button', { name: 'Ajustar horários da semana' }).count(), 0)
    assert.equal(await restrito.page.getByRole('button', { name: 'Salvar configuração', exact: true }).count(), 0)
    assert.deepEqual(restrito.estado.escritas, [])
    await restrito.context.close()
  })
  writeFileSync(join(pasta, 'resultado.json'), JSON.stringify({ resultados, total: resultados.length }, null, 2))
  console.log(`${resultados.length} cenários de navegador aprovados; API controlada e nenhuma escrita na loja.`)
} catch (erro) {
  if (ultimaPagina && !ultimaPagina.isClosed()) await foto(ultimaPagina, 'falha')
  throw erro
} finally {
  await browser.close()
}
