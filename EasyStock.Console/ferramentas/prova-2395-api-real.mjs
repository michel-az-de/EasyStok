// Opt-in: aplicação inteira, login real e PostgreSQL da CadastrosR4BrowserFixture.
// Não intercepta HTTP. Apenas a fixture substitui os provedores externos.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const fixture = JSON.parse(readFileSync(process.env.R4_FIXTURE ?? '../.build/r4-browser.json', 'utf8'))
const origem = process.env.CONSOLE_TEST_URL
const soFinanceiro = process.env.R4_ETAPA === 'financeiro'
assert.ok(origem, 'Informe CONSOLE_TEST_URL com Vite apontando para a API da fixture.')
const pasta = resolve(import.meta.dirname, '../.build/2395/real')
mkdirSync(pasta, { recursive: true })
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'pt-BR', reducedMotion: 'reduce' })
const page = await context.newPage()
page.setDefaultTimeout(20000)
const resultados = [], erros = [], chamadas = []
let etapa = 'login'
page.on('pageerror', e => erros.push(e.message))
page.on('response', r => { const caminho = new URL(r.url()).pathname; if (caminho.startsWith('/api/')) chamadas.push({ caminho, metodo: r.request().method(), status: r.status() }) })
const registro = (caso, dados = {}) => { resultados.push({ caso, ...dados }); console.log(`ok ${caso}`) }
async function login(pagina, identidade, rota = '#/m/atendimento/balcao') {
  await pagina.goto(origem + '/' + rota)
  await pagina.getByRole('textbox', { name: 'E-mail', exact: true }).fill(identidade.email)
  await pagina.getByLabel('Senha', { exact: true }).fill(identidade.senha)
  await pagina.getByRole('button', { name: 'Entrar', exact: true }).click()
  await pagina.getByRole('textbox', { name: 'E-mail', exact: true }).waitFor({ state: 'hidden' })
}
async function ler(pagina, caminho) {
  const r = await pagina.evaluate(async caminho => {
    const s = JSON.parse(sessionStorage.getItem('easystok.sessao') ?? localStorage.getItem('easystok.sessao'))
    const resposta = await fetch(caminho, { headers: { Authorization: `Bearer ${s.token}` } })
    const texto = await resposta.text()
    return { status: resposta.status, json: texto ? JSON.parse(texto) : null }
  }, caminho)
  return r
}
async function link(pagina, href) {
  let alvo = pagina.locator(`a[href="${href}"]:visible`).first()
  if (!await alvo.count()) {
    const menu = pagina.getByRole('button', { name: /^Menu de / })
    if (await menu.count()) await menu.click()
    alvo = pagina.locator(`a[href="${href}"]:visible`).first()
  }
  if (!await alvo.count()) {
    await pagina.locator('a[href="#/"]:visible').first().click()
    alvo = pagina.locator(`a[href="${href}"]:visible`).first()
  }
  await alvo.click()
}
async function abrirConversa(nome) {
  const todas = page.getByRole('button', { name: /^Todas\b/ })
  if (await todas.count()) await todas.click()
  await page.getByRole('button', { name: new RegExp(`^${nome}\\b`) }).first().click()
  await page.getByRole('button', { name: 'Conferir identidade', exact: true }).waitFor()
}
async function confirmarIdentidade(acao, motivo) {
  const dialogo = page.getByRole('dialog', { name: 'Conferir identidade da conversa', exact: true })
  if (acao) await dialogo.getByRole('button', { name: acao }).click()
  await dialogo.getByRole('textbox', { name: 'Motivo da alteração' }).fill(motivo)
  await dialogo.getByRole('checkbox', { name: 'Conferi a identidade com a pessoa e revisei esta alteração.' }).check()
  const resposta = page.waitForResponse(r => r.url().endsWith('/vinculo-cliente') && r.request().method() === 'POST')
  await dialogo.getByRole('button', { name: 'Confirmar alteração', exact: true }).click()
  const recebida = await resposta
  assert.equal(recebida.status(), 200, await recebida.text())
  await dialogo.getByText('Vínculo atualizado e registrado no histórico.', { exact: true }).waitFor()
  return dialogo
}
try {
  const dona = fixture.identidades.find(i => i.empresaId === fixture.empresaId && i.perfil === 'Dona')
  await login(page, dona, soFinanceiro ? '#/m/cadastros/bancos' : '#/m/atendimento/balcao')
  if (!soFinanceiro) {
  await abrirConversa('Ana Pereira')
  registro('Login real pela tela e conversa da fixture aberta')

  etapa = 'identidade'
  const rascunho = 'Rascunho R4 preservado, sem envio.'
  await page.getByRole('textbox', { name: 'Mensagem para o cliente' }).fill(rascunho)
  const estadoInicial = await ler(page, `/api/atendimento/conversas/${fixture.conversaId}/vinculo-cliente`)
  const historicoInicial = estadoInicial.json.data.historico.length
  await page.getByRole('button', { name: 'Conferir identidade', exact: true }).click()
  let dialogo = await confirmarIdentidade('Retirar vínculo com Ana Pereira', 'Identidade conferida no ensaio R4: retirar vínculo')
  let vinculo = await ler(page, `/api/atendimento/conversas/${fixture.conversaId}/vinculo-cliente`)
  assert.equal(vinculo.json.data.clienteId, null)
  assert.equal(vinculo.json.data.historico.length, historicoInicial + 1)
  dialogo = await confirmarIdentidade('Restaurar vínculo com Ana Pereira', 'Identidade conferida no ensaio R4: restaurar vínculo')
  vinculo = await ler(page, `/api/atendimento/conversas/${fixture.conversaId}/vinculo-cliente`)
  assert.equal(vinculo.json.data.clienteId, fixture.clienteId)
  assert.equal(vinculo.json.data.historico.length, historicoInicial + 2)
  await dialogo.getByRole('button', { name: 'Fechar', exact: true }).click()
  assert.equal(await page.getByRole('textbox', { name: 'Mensagem para o cliente' }).inputValue(), rascunho)
  registro('Retirada e restauração transacionais preservam rascunho', { conversaId: fixture.conversaId, revisao: vinculo.json.data.vinculoRevisao })

  await abrirConversa('Novo contato')
  await page.getByRole('button', { name: 'Conferir identidade', exact: true }).click()
  dialogo = page.getByRole('dialog', { name: 'Conferir identidade da conversa', exact: true })
  await dialogo.getByRole('textbox', { name: 'Telefone para conferir' }).fill('(11) 99999-8888')
  await dialogo.getByRole('button', { name: 'Consultar cadastros', exact: true }).click()
  await dialogo.getByRole('radio', { name: /Bruno Pereira/ }).waitFor()
  assert.equal(await dialogo.getByRole('radio').count(), 3)
  await dialogo.getByRole('radio', { name: /Bruno Pereira/ }).check()
  await confirmarIdentidade(null, 'Pessoa conferida entre três cadastros do mesmo telefone')
  vinculo = await ler(page, `/api/atendimento/conversas/${fixture.conversaSemCadastroId}/vinculo-cliente`)
  assert.equal(vinculo.json.data.clienteNome, 'Bruno Pereira')
  await confirmarIdentidade('Retirar vínculo com Bruno Pereira', 'Encerrar ensaio sem atribuir identidade ao contato')
  await dialogo.getByRole('button', { name: 'Fechar', exact: true }).click()
  registro('Três candidatos reais e vínculo somente com escolha explícita', { conversaId: fixture.conversaSemCadastroId })

  etapa = 'cadastro-ia-contexto'
  await abrirConversa('Ana Pereira')
  await link(page, '#/m/cadastros')
  await page.getByRole('button', { name: 'Conferir cadastro', exact: true }).click()
  const editor = page.getByRole('dialog').last()
  await editor.getByText('Usa até 20 mensagens de texto recentes recebidas nesta conversa para sugerir nome, telefone e e-mail.', { exact: true }).waitFor()
  const inferencia = page.waitForResponse(r => r.url().endsWith('/assistente/cadastro') && r.request().method() === 'POST')
  await editor.getByRole('button', { name: 'Consultar com IA', exact: true }).click()
  assert.equal((await inferencia).status(), 200)
  await editor.getByRole('button', { name: 'Usar este dado', exact: true }).first().waitFor()
  assert.equal(await editor.getByRole('button', { name: 'Usar este dado', exact: true }).count(), 3)
  for (let i = 0; i < 3; i++) await editor.getByRole('button', { name: 'Usar este dado', exact: true }).first().click()
  assert.equal(await editor.getByRole('textbox', { name: 'E-mail', exact: true }).inputValue(), 'ana@fixture.test')
  await editor.getByRole('button', { name: 'Salvar cadastro', exact: true }).click()
  await editor.waitFor({ state: 'hidden' })
  const cadastro = await ler(page, `/api/clientes/${fixture.clienteId}?empresaId=${fixture.empresaId}`)
  assert.equal(cadastro.json.data.cliente.email, 'ana@fixture.test')
  const busca = page.getByRole('combobox', { name: 'Buscar', exact: true })
  await busca.fill('(11) 99999-8888')
  await page.getByRole('option', { name: /Ana Pereira/ }).waitFor()
  assert.equal(await page.getByRole('option').count(), 3)
  await busca.press('Escape')
  await page.getByRole('button', { name: 'Leads', exact: true }).click()
  await page.getByRole('button', { name: 'Editar Ana Pereira', exact: true }).waitFor()
  await page.getByRole('button', { name: 'Voltar à conversa', exact: true }).click()
  assert.equal(await page.getByRole('textbox', { name: 'Mensagem para o cliente' }).inputValue(), rascunho)
  await link(page, '#/m/cadastros')
  await page.getByRole('button', { name: 'Conferir cadastro', exact: true }).waitFor()
  await page.getByRole('button', { name: 'Editar Ana Pereira', exact: true }).waitFor()
  assert.equal(await busca.inputValue(), '(11) 99999-8888')
  assert.equal(await page.getByRole('button', { name: 'Leads', exact: true }).getAttribute('aria-pressed'), 'true')
  registro('API e harness reais com LLM controlado, aplicação individual, autocomplete e retorno contextual', { clienteId: fixture.clienteId })
  await page.getByRole('button', { name: 'Sem cadastro', exact: true }).click()
  await page.getByRole('textbox', { name: 'Buscar', exact: true }).fill('(11) 91234-5678')
  const contato = page.getByRole('row').filter({ hasText: '5511912345678' })
  await contato.waitFor()
  assert.equal(await contato.count(), 1)
  assert.ok((await contato.innerText()).includes('Sem cadastro'))
  registro('Sem cadastro exibe contato WhatsApp canônico e encontra telefone formatado')
  writeFileSync(resolve(pasta, 'identidade-liberada.json'), JSON.stringify({ conversaId: fixture.conversaId, prontaParaPedido: true }))
  }

  etapa = 'financeiro'
  await link(page, '#/m/cadastros/bancos')
  const nomeConta = `Conta R4 ${Date.now()}`
  await page.getByRole('button', { name: 'Novo cadastro', exact: true }).click()
  await page.getByRole('textbox', { name: 'Nome da conta', exact: true }).fill(nomeConta)
  await page.getByRole('textbox', { name: 'Instituição financeira', exact: true }).fill('Instituição fictícia')
  await page.getByRole('textbox', { name: 'Agência', exact: true }).fill('0001')
  await page.getByRole('textbox', { name: 'Número da conta', exact: true }).fill('123-4')
  await page.getByRole('textbox', { name: 'Titular', exact: true }).fill('Casa da Babá Fixture')
  await page.getByRole('button', { name: 'Salvar cadastro', exact: true }).click()
  await page.getByRole('button', { name: `Editar ${nomeConta}`, exact: true }).waitFor()
  await page.reload()
  await page.getByRole('button', { name: `Editar ${nomeConta}`, exact: true }).click()
  assert.equal(await page.getByRole('textbox', { name: 'Número da conta', exact: true }).inputValue(), '123-4')
  await page.getByRole('checkbox', { name: 'Conta ativa', exact: true }).uncheck()
  await page.getByRole('button', { name: 'Salvar cadastro', exact: true }).click()
  await page.getByRole('dialog').waitFor({ state: 'hidden' })
  await page.getByRole('button', { name: `Editar ${nomeConta}`, exact: true }).waitFor()
  const contas = await ler(page, '/api/cadastros/contas-bancarias')
  const conta = contas.json.data.find(c => c.nome === nomeConta)
  assert.equal(conta.ativo, false)
  await context.setOffline(true)
  await page.getByRole('button', { name: 'Atualizar', exact: true }).click()
  await page.getByRole('button', { name: 'Tentar de novo', exact: true }).waitFor()
  await context.setOffline(false)
  await page.getByRole('button', { name: 'Tentar de novo', exact: true }).click()
  await page.getByRole('button', { name: `Editar ${nomeConta}`, exact: true }).waitFor()
  registro('Conta criada, recarregada, inativada e recuperada após rede offline', { contaId: conta.id, revisao: conta.revisao })

  await link(page, '#/m/cadastros/pagamentos')
  const metodos = (await ler(page, '/api/cadastros/metodos-pagamento')).json.data
  assert.equal(metodos.length, 6)
  const metodo = metodos.find(m => m.codigo === 'debito' && m.ativo)
  assert.ok(metodo)
  const linhaMetodo = page.getByRole('listitem').filter({ has: page.getByRole('heading', { name: metodo.nome, exact: true }) })
  await linhaMetodo.getByRole('button', { name: 'Inativar', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Confirmar alteração', exact: true }).click()
  await linhaMetodo.getByRole('button', { name: 'Ativar', exact: true }).waitFor()
  await page.reload()
  await linhaMetodo.getByRole('button', { name: 'Ativar', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Confirmar alteração', exact: true }).click()
  await linhaMetodo.getByRole('button', { name: 'Inativar', exact: true }).waitFor()
  const metodoFinal = (await ler(page, '/api/cadastros/metodos-pagamento')).json.data.find(m => m.codigo === metodo.codigo)
  assert.equal(metodoFinal.ativo, true)
  assert.equal(metodoFinal.revisao, metodo.revisao + 2)
  registro('Seis métodos reais, inativação persistida e reativação confirmada', { codigo: metodo.codigo, revisao: metodoFinal.revisao })

  etapa = 'mobile-permissoes'
  await link(page, '#/m/cadastros/bancos')
  await page.getByRole('button', { name: `Editar ${nomeConta}`, exact: true }).waitFor()
  await page.setViewportSize({ width: 390, height: 844 })
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
  await page.screenshot({ path: resolve(pasta, 'bancos-390.png'), fullPage: true })
  const restrito = await browser.newContext({ viewport: { width: 1440, height: 1000 } })
  const consulta = await restrito.newPage()
  await login(consulta, fixture.identidades.find(i => i.perfil === 'Atendimento'), '#/m/cadastros/clientes')
  await consulta.getByRole('heading', { name: 'Leads e clientes', exact: true }).first().waitFor()
  assert.equal(await consulta.locator('a[href="#/m/cadastros/bancos"]').count(), 0)
  assert.equal((await ler(consulta, '/api/cadastros/contas-bancarias')).status, 403)
  await restrito.close()
  registro('390 px sem overflow; Atendimento sem tela nem API financeira')
  etapa = 'rotas-invalidas'
  for (const rota of ['#/m/invalido/tela', '#/m/cadastros/invalida']) {
    await page.goto(origem + '/' + rota)
    await page.getByRole('button', { name: 'Voltar ao início', exact: true }).waitFor()
    await page.screenshot({ path: resolve(pasta, rota.includes('/cadastros/') ? 'tela-invalida.png' : 'modulo-invalido.png'), fullPage: true })
    await page.getByRole('button', { name: 'Voltar ao início', exact: true }).click()
    await page.waitForURL(url => url.hash === '#/')
  }
  registro('Rotas desconhecidas informadas com recuperação explícita ao início')
  assert.deepEqual(erros, [])
} catch (e) {
  await context.setOffline(false)
  await page.screenshot({ path: resolve(pasta, `falha-${etapa}.png`), fullPage: true })
  writeFileSync(resolve(pasta, `falha-${etapa}.txt`), (await page.locator('body').innerText()).slice(0, 15000))
  throw e
} finally {
  writeFileSync(resolve(pasta, soFinanceiro ? 'resultado-financeiro.json' : 'resultado.json'), JSON.stringify({ escopo: 'UI completa + API JWT + PostgreSQL reais; provedores controlados pela fixture, sem homologação externa', etapa, resultados, erros, chamadas }, null, 2))
  await context.close(); await browser.close()
}
