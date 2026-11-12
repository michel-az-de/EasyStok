// M01: a API executa o envio real no laboratório; só sua resposta ao navegador é perdida.
// Nenhum endpoint de falha é criado. As outras chamadas diretas são leituras de reconciliação.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'

assert.equal(process.env.R4_EXECUTAR_MENSAGEM, '1', 'Execute somente após liberação da raiz, com R4_EXECUTAR_MENSAGEM=1')
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const fixture = JSON.parse(readFileSync(process.env.R4_FIXTURE ?? '../.build/r4-browser.json', 'utf8'))
const origem = process.env.CONSOLE_TEST_URL
assert.ok(origem && fixture.conversaConferenciaId)
for (const url of [origem, fixture.api]) assert.ok(['localhost', '127.0.0.1'].includes(new URL(url).hostname))
assert.match(fixture.provedores, /controlados/)
const conversaId = fixture.conversaConferenciaId
const rotaMensagens = `/api/atendimento/conversas/${conversaId}/mensagens`
const texto = `Conferência de mensagem R4 ${new Date().toISOString()}: confirme o recebimento desta mensagem de teste.`
const pasta = resolve(import.meta.dirname, '../.build/2395/real')
mkdirSync(pasta, { recursive: true })
const prova = { conversaId, falhaControlada: 'Resposta HTTP perdida depois do 200 real da API', resultados: [], envios: [], erros: [] }
const salvar = () => writeFileSync(resolve(pasta, 'mensagem-ui.json'), JSON.stringify(prova, null, 2))
const registrar = (caso, dados = {}) => { prova.resultados.push({ caso, ...dados }); salvar(); console.log(`ok ${caso}`) }
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'pt-BR', reducedMotion: 'reduce' })
const page = await context.newPage()
page.setDefaultTimeout(25000)
page.on('pageerror', erro => prova.erros.push(erro.message))
let etapa = 'login'
let perderResposta = false
let confirmarPerda, recusarPerda
const perda = new Promise((resolve, reject) => { confirmarPerda = resolve; recusarPerda = reject })
// A rejeição é observada no passo de envio; evita unhandled rejection enquanto o clique termina.
perda.catch(() => {})
async function ler(caminho) {
  return page.evaluate(async caminho => {
    const s = JSON.parse(sessionStorage.getItem('easystok.sessao') ?? localStorage.getItem('easystok.sessao'))
    const resposta = await fetch(caminho, { headers: { Authorization: `Bearer ${s.token}` } })
    const texto = await resposta.text()
    if (!resposta.ok) throw new Error(`GET ${caminho}: ${resposta.status} ${texto}`)
    return texto ? JSON.parse(texto).data : null
  }, caminho)
}
await page.route('**/api/atendimento/conversas/*/mensagens', async route => {
  const request = route.request()
  if (request.method() !== 'POST') return route.continue()
  if (new URL(request.url()).pathname !== rotaMensagens) {
    recusarPerda(new Error('A UI tentou enviar para outra conversa'))
    return route.abort('blockedbyclient')
  }
  const envio = { chave: request.headers()['idempotency-key'], corpo: request.postDataJSON() }
  prova.envios.push(envio)
  if (!perderResposta) return route.continue()
  perderResposta = false
  try {
    const resposta = await route.fetch()
    const corpo = await resposta.json()
    assert.equal(resposta.status(), 200, JSON.stringify(corpo))
    assert.equal(corpo.data.texto, texto)
    assert.ok(corpo.data.id)
    prova.respostaPerdida = { status: resposta.status(), mensagem: corpo.data }
    salvar()
    await route.abort('failed')
    confirmarPerda(corpo.data)
  } catch (erro) {
    await route.abort('failed').catch(() => {})
    recusarPerda(erro)
  }
})
try {
  const dona = fixture.identidades.find(i => i.perfil === 'Dona' && i.empresaId === fixture.empresaId)
  assert.ok(dona)
  await page.goto(origem + '/#/m/atendimento/balcao')
  await page.getByLabel('E-mail', { exact: true }).fill(dona.email)
  await page.getByLabel('Senha', { exact: true }).fill(dona.senha)
  const autenticando = page.waitForResponse(r => new URL(r.url()).pathname === '/api/auth/login' && r.request().method() === 'POST')
  await page.getByRole('button', { name: 'Entrar', exact: true }).click()
  assert.equal((await autenticando).status(), 200)
  const todas = page.getByRole('button', { name: /^Todas\b/ })
  await todas.waitFor(); await todas.click()
  const lista = await ler('/api/atendimento/conversas?limite=200')
  const conversa = lista.find(c => c.id === conversaId)
  assert.ok(conversa, 'A conversa de conferência precisa estar visível nesta empresa')
  assert.ok(conversa.clienteId && !conversa.vinculoEmRevisao, 'Conferir/restaurar a identidade antes desta jornada')
  assert.equal(await ler(`/api/atendimento/conversas/${conversaId}/pedido`), null, 'Massa isolada sem pedido em andamento')
  const antes = await ler(`${rotaMensagens}?limite=200`)
  assert.equal(antes.length, Number(process.env.R4_MENSAGENS_ANTES ?? 0), 'Informe a contagem real autorizada antes de repetir a prova')
  await page.getByPlaceholder('Buscar conversa', { exact: true }).fill(conversa.contatoNome || conversa.contatoIdExterno)
  const cartao = page.locator('button[aria-current]')
  await cartao.first().waitFor()
  assert.equal(await cartao.count(), 1, 'Busca pelo nome real do contato deve selecionar uma única conversa')
  prova.destinatario = { contato: conversa.contatoIdExterno, nome: conversa.contatoNome, cartao: await cartao.getAttribute('aria-label') }
  await cartao.click()
  const campo = page.getByRole('textbox', { name: 'Mensagem para o cliente', exact: true })
  await campo.waitFor()
  assert.equal(await campo.isEnabled(), true)

  etapa = 'variável ausente'
  await campo.fill('Seu pedido é {pedido}.')
  const enviar = page.getByRole('button', { name: 'Enviar mensagem', exact: true })
  assert.equal(await enviar.isDisabled(), true)
  await page.getByRole('alert').filter({ hasText: 'apague {pedido}' }).waitFor()
  assert.equal(prova.envios.length, 0)
  assert.deepEqual(await ler(`${rotaMensagens}?limite=200`), antes)
  await page.screenshot({ path: resolve(pasta, 'mensagem-variavel-ausente.png'), fullPage: true })
  registrar('Variável de pedido sem valor impede envio e não grava mensagem')

  etapa = 'prévia e destinatário'
  await campo.fill(texto)
  assert.equal(await campo.inputValue(), texto)
  assert.equal(await enviar.isEnabled(), true)
  await page.screenshot({ path: resolve(pasta, 'mensagem-previa-destinatario.png'), fullPage: true })
  registrar('Texto e destinatário visíveis antes do envio', prova.destinatario)

  etapa = 'resposta perdida após commit'
  perderResposta = true
  await enviar.click()
  const primeira = await perda
  await page.getByRole('alert').filter({ hasText: 'Não enviada: Sem conexão com o EasyStok.' }).first().waitFor()
  await page.screenshot({ path: resolve(pasta, 'mensagem-resposta-perdida.png'), fullPage: true })
  const depoisDaPerda = await ler(`${rotaMensagens}?limite=200`)
  assert.equal(depoisDaPerda.filter(m => m.texto === texto && m.direcao === 'Saida').length, 1)
  assert.equal(depoisDaPerda.find(m => m.texto === texto).id, primeira.id)
  registrar('API gravou uma mensagem antes da perda controlada da resposta', { mensagemId: primeira.id })

  etapa = 'repetição pela UI'
  await campo.fill(texto)
  const repetindo = page.waitForResponse(r => new URL(r.url()).pathname === rotaMensagens && r.request().method() === 'POST')
  await enviar.click()
  const repetida = await repetindo
  const segunda = await repetida.json()
  assert.equal(repetida.status(), 200, JSON.stringify(segunda))
  assert.equal(repetida.headers()['x-idempotent-replay'], 'true')
  assert.equal(segunda.data.id, primeira.id)
  assert.equal(prova.envios.length, 2)
  assert.match(prova.envios[0].chave, /^[0-9a-f-]{36}$/i)
  assert.deepEqual(prova.envios[1], prova.envios[0], 'Mesmo corpo e mesma chave para a intenção ainda não confirmada')
  const depois = await ler(`${rotaMensagens}?limite=200`)
  assert.equal(depois.length, antes.length + 1)
  assert.equal(depois.filter(m => m.texto === texto).length, 1)
  assert.equal(depois.find(m => m.texto === texto).id, primeira.id)
  const prazoDaTela = Date.now() + 10000
  while (await page.getByText(texto, { exact: true }).count() !== 1 && Date.now() < prazoDaTela) await page.waitForTimeout(100)
  assert.equal(await page.getByText(texto, { exact: true }).count(), 1, 'Uma intenção confirmada deve deixar somente um balão na tela')
  assert.equal(await page.getByRole('alert').filter({ hasText: 'Não enviada: Sem conexão com o EasyStok.' }).count(), 0,
    'O replay confirmado resolve o aviso da tentativa cuja resposta foi perdida')
  await page.screenshot({ path: resolve(pasta, 'mensagem-repeticao-confirmada.png'), fullPage: true })
  registrar('Repetição conserva Idempotency-Key, retorna mesmo ID e deixa uma única mensagem persistida', { mensagemId: primeira.id })

  etapa = 'disponibilidade de modelos'
  const modelos = page.getByRole('button', { name: 'Modelos aprovados', exact: true })
  if (await modelos.count() && await modelos.first().isVisible()) {
    const consultando = page.waitForResponse(r => new URL(r.url()).pathname === '/api/atendimento/modelos' && r.request().method() === 'GET')
    await modelos.first().click()
    const resposta = await consultando
    const catalogo = await resposta.json()
    prova.catalogo = { status: resposta.status(), codigo: catalogo.error?.code, quantidade: catalogo.data?.length }
    await page.getByRole('dialog', { name: 'Enviar modelo aprovado', exact: true }).waitFor()
    if (!resposta.ok() || catalogo.data?.length === 0) {
      assert.equal(await page.getByRole('button', { name: 'Enviar modelo', exact: true }).isDisabled(), true)
      registrar('Catálogo realmente indisponível/vazio impede enviar modelo', prova.catalogo)
    } else prova.limitacaoModelos = 'Catálogo disponível: não há massa controlada de envio/modelo para esta jornada; nenhum modelo enviado.'
    await page.screenshot({ path: resolve(pasta, 'mensagem-modelos.png'), fullPage: true })
  } else prova.limitacaoModelos = 'Conversa com janela aberta não oferece modelo neste estado. Exige massa de janela encerrada ou próxima do limite e catálogo/provider controlados.'
  assert.deepEqual(prova.erros, [])
  prova.sucesso = true
  salvar()
} catch (erro) {
  prova.falha = { etapa, mensagem: erro.message }
  salvar()
  await page.screenshot({ path: resolve(pasta, 'mensagem-falha.png'), fullPage: true }).catch(() => {})
  throw erro
} finally {
  await browser.close()
}
