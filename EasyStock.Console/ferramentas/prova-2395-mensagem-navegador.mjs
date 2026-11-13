// M01: a API executa o envio real no laboratório; só sua resposta ao navegador é perdida.
// Nenhum endpoint de falha é criado. As outras chamadas diretas são leituras de reconciliação.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync, readdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'

assert.equal(process.env.R4_EXECUTAR_MENSAGEM, '1', 'Execute somente após liberação da raiz, com R4_EXECUTAR_MENSAGEM=1')
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const fixture = JSON.parse(readFileSync(process.env.R4_FIXTURE ?? '../.build/r4-browser.json', 'utf8'))
const origem = process.env.CONSOLE_TEST_URL
assert.ok(origem && fixture.conversaConferenciaId)
for (const url of [origem, fixture.api]) assert.ok(['localhost', '127.0.0.1'].includes(new URL(url).hostname))
assert.match(fixture.provedores, /controlados/)
const conversaId = fixture.conversaConferenciaId
const fase = process.env.R4_FASE_M01
assert.ok(['red', 'green'].includes(fase), 'Informe R4_FASE_M01=red ou green')
const rodada = process.env.R4_RODADA_M01 ?? ''
assert.match(rodada, /^[a-z0-9-]*$/, 'Rodada opcional deve ser um identificador simples')
assert.ok(process.env.R4_BACKEND_REVISAO, 'Registre a revisão do binário servido em R4_BACKEND_REVISAO')
assert.match(process.env.R4_BACKEND_SHA256 ?? '', /^[0-9a-f]{64}$/i, 'Registre o SHA256 do binário servido em R4_BACKEND_SHA256')
const rotaMensagens = `/api/atendimento/conversas/${conversaId}/mensagens`
const texto = `Conferência de mensagem R4 ${new Date().toISOString()}: confirme o recebimento desta mensagem de teste.`
const pasta = resolve(import.meta.dirname, `../.build/2395/real/m01-intermediario/${fase}${rodada ? `-${rodada}` : ''}`)
mkdirSync(pasta, { recursive: true })
const sha256 = dados => createHash('sha256').update(dados).digest('hex')
const prova = { fase, conversaId, texto, versao: { head: execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim(), roteiroSha256: sha256(readFileSync(import.meta.filename)), backend: process.env.R4_BACKEND_REVISAO, backendSha256: process.env.R4_BACKEND_SHA256, fixtureSha256: sha256(readFileSync(process.env.R4_FIXTURE ?? '../.build/r4-browser.json')) }, falhaControlada: 'Resposta perdida depois do 200 real; ACK da retentativa retido, GET/sync naturais ativos', resultados: [], envios: [], sincronizacoes: [], violacoes: [], erros: [] }
const salvar = () => writeFileSync(resolve(pasta, 'mensagem-ui.json'), JSON.stringify(prova, null, 2))
const registrar = (caso, dados = {}) => { prova.resultados.push({ caso, ...dados }); salvar(); console.log(`${dados.esperados != null && dados.observados !== dados.esperados ? 'falhou' : 'ok'} ${caso}`) }
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'pt-BR', reducedMotion: 'reduce' })
const page = await context.newPage()
page.setDefaultTimeout(25000)
page.on('pageerror', erro => prova.erros.push(erro.message))
let etapa = 'login'
let perderResposta = false
let reterResposta = false
let liberarAck, confirmarRetencao, recusarRetencao
const liberar = new Promise(resolve => { liberarAck = resolve })
const retida = new Promise((resolve, reject) => { confirmarRetencao = resolve; recusarRetencao = reject }); retida.catch(() => {})
let confirmarPerda, recusarPerda
const perda = new Promise((resolve, reject) => { confirmarPerda = resolve; recusarPerda = reject })
// A rejeição é observada no passo de envio; evita unhandled rejection enquanto o clique termina.
perda.catch(() => {})
const modulos = ['src/aplicacao/acoesApi.js', 'src/aplicacao/chavesDeMensagem.js', 'src/aplicacao/casos/atendimento.js', 'src/aplicacao/casos/api.js', 'src/aplicacao/reducer/mesclaDoServidor.js', 'src/infra/api/traducaoConversas.js']
const hashDoConteudo = sha256
async function hashesServidos() {
  const resposta = await page.request.get(origem + '/'); assert.ok(resposta.ok())
  const html = await resposta.text()
  const meta = [...html.matchAll(/<meta\b[^>]*>/gi)].map(m => m[0]).find(m => /name=["']easystok-console-sha["']/i.test(m))
  const fontesLocais = Object.fromEntries(modulos.map(m => [m, hashDoConteudo(readFileSync(m))]))
  if (meta) {
    const shaDeclarado = meta.match(/content=["']([^"']+)["']/i)?.[1]
    assert.equal(shaDeclarado, execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim(), 'HTML deve declarar o HEAD da candidata')
    const nomes = readdirSync('dist/assets').filter(n => /\.(js|css)$/.test(n)).sort()
    assert.ok(nomes.length > 0, 'Build local precisa dos assets publicados')
    const assetsServidos = {}
    for (const nome of nomes) {
      const r = await page.request.get(`${origem}/assets/${nome}`); assert.ok(r.ok())
      const servido = hashDoConteudo(await r.body()), local = hashDoConteudo(readFileSync(`dist/assets/${nome}`))
      assert.equal(servido, local, `Asset servido deve ser idêntico ao build local: ${nome}`)
      assetsServidos[nome] = { servido, local }
    }
    return { modo: 'build', shaDeclarado, htmlSha256: hashDoConteudo(html), fontesLocais, assetsServidos }
  }
  assert.notEqual(process.env.R4_CONSOLE_BUILD, '1', 'Build sem meta easystok-console-sha não serve como prova de candidata')
  const modulosServidos = {}
  for (const modulo of modulos) {
    const r = await page.request.get(`${origem}/${modulo}`); assert.ok(r.ok())
    assert.match(r.headers()['content-type'] ?? '', /javascript/, 'Vite dev deve servir JavaScript; HTML de fallback não é um módulo')
    modulosServidos[modulo] = hashDoConteudo(await r.body())
  }
  return { modo: 'dev', fontesLocais, modulosServidos }
}
page.on('response', async r => {
  const u = new URL(r.url())
  if (r.request().method() !== 'GET' || u.pathname !== rotaMensagens || u.searchParams.get('limite') !== '100' || !r.ok()) return
  try { const j = await r.json(); prova.sincronizacoes.push({ em: Date.now(), ids: j.data.map(m => m.id), chaves: j.data.map(m => m.chaveEnvio ?? null) }); salvar() } catch { /* Aba pode fechar durante leitura. */ }
})
async function esperarSync(mensagemId, desde) {
  const limite = Date.now() + 45000
  while (!prova.sincronizacoes.some(s => s.em > desde && s.ids.includes(mensagemId)) && Date.now() < limite) await page.waitForTimeout(250)
  assert.ok(prova.sincronizacoes.some(s => s.em > desde && s.ids.includes(mensagemId)), 'GET natural da aplicação trouxe a intenção persistida')
  await page.waitForTimeout(250)
}
async function conferirBaloes(nome, esperados) {
  const baloes = await page.getByText(texto, { exact: true }).evaluateAll(nos => nos.map(no => ({ texto: no.textContent, estado: no.parentElement.innerText })))
  const dado = { etapa: nome, esperados, observados: baloes.length, baloes }
  if (baloes.length !== esperados) prova.violacoes.push(dado)
  registrar(nome, dado)
  await page.screenshot({ path: resolve(pasta, `${nome}.png`), fullPage: true })
}
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
  if (!perderResposta && !reterResposta) return route.continue()
  const retendo = reterResposta
  perderResposta = false
  reterResposta = false
  try {
    const resposta = await route.fetch()
    const corpo = await resposta.json()
    assert.equal(resposta.status(), 200, JSON.stringify(corpo))
    assert.equal(corpo.data.texto, texto)
    assert.ok(corpo.data.id)
    if (retendo) {
      prova.ackRetido = { status: resposta.status(), replay: resposta.headers()['x-idempotent-replay'], mensagem: corpo.data }
      salvar(); confirmarRetencao(corpo.data)
      await liberar
      return route.fulfill({ response: resposta })
    }
    prova.respostaPerdida = { status: resposta.status(), mensagem: corpo.data }
    salvar()
    await route.abort('failed')
    confirmarPerda(corpo.data)
  } catch (erro) {
    await route.abort('failed').catch(() => {})
    recusarPerda(erro)
    recusarRetencao(erro)
  }
})
try {
  prova.versao.inicial = await hashesServidos(); salvar()
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
  await page.getByPlaceholder('Buscar conversa', { exact: true }).fill(`comanda ${conversa.numeroComanda}`)
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
  const inicioPerda = Date.now()
  perderResposta = true
  await enviar.click()
  const primeira = await perda
  if (fase === 'green') assert.equal(primeira.chaveEnvio, prova.envios[0].chave, 'POST real expõe a correlação durável da intenção')
  await page.getByText(texto, { exact: true }).first().waitFor()
  await page.screenshot({ path: resolve(pasta, 'mensagem-resposta-perdida.png'), fullPage: true })
  const depoisDaPerda = await ler(`${rotaMensagens}?limite=200`)
  assert.equal(depoisDaPerda.filter(m => m.texto === texto && m.direcao === 'Saida').length, 1)
  assert.equal(depoisDaPerda.find(m => m.texto === texto).id, primeira.id)
  registrar('API gravou uma mensagem antes da perda controlada da resposta', { mensagemId: primeira.id })
  await esperarSync(primeira.id, inicioPerda)
  await conferirBaloes('apos-perda-e-sync', 1)

  etapa = 'repetição pela UI'
  reterResposta = true
  await campo.fill(texto)
  const repetindo = page.waitForResponse(r => new URL(r.url()).pathname === rotaMensagens && r.request().method() === 'POST', { timeout: 60000 })
  repetindo.catch(() => {})
  await enviar.click()
  const ack = await retida
  assert.equal(ack.id, primeira.id)
  assert.equal(prova.ackRetido.replay, 'true')
  await conferirBaloes('retry-ack-retido', 1)
  const inicioSyncRetido = Date.now()
  await esperarSync(primeira.id, inicioSyncRetido)
  const duranteRetencao = await ler(`${rotaMensagens}?limite=200`)
  assert.equal(duranteRetencao.filter(m => m.texto === texto).length, 1)
  await conferirBaloes('retry-ack-retido-apos-sync', 1)
  liberarAck()
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
  await conferirBaloes('apos-ack', 1)

  etapa = 'mensagem legítima com texto igual'
  await campo.fill(texto)
  const novaResposta = page.waitForResponse(r => new URL(r.url()).pathname === rotaMensagens && r.request().method() === 'POST')
  await enviar.click()
  const nova = await novaResposta; assert.equal(nova.status(), 200)
  const novaMensagem = (await nova.json()).data
  assert.notEqual(novaMensagem.id, primeira.id)
  assert.notEqual(prova.envios[2].chave, prova.envios[0].chave)
  await esperarSync(novaMensagem.id, inicioSyncRetido)
  const iguais = (await ler(`${rotaMensagens}?limite=200`)).filter(m => m.texto === texto)
  assert.equal(iguais.length, 2)
  await conferirBaloes('duas-intencoes-texto-igual', 2)
  prova.mensagensIguais = iguais.map(m => ({ id: m.id, chaveEnvio: m.chaveEnvio ?? null }))

  etapa = 'disponibilidade de modelos'
  if (fase === 'green') assert.ok(fixture.conversaModeloId, 'Green exige conversa isolada com janela encerrada para modelos')
  if (fixture.conversaModeloId) {
    const inbox = await ler('/api/atendimento/conversas?limite=100&pagina=1')
    const fechada = inbox.find(c => c.id === fixture.conversaModeloId); assert.ok(fechada)
    await page.getByPlaceholder('Buscar conversa', { exact: true }).fill(`comanda ${fechada.numeroComanda}`)
    await page.getByRole('button', { name: new RegExp(`Comanda ${fechada.numeroComanda},`) }).click()
    assert.equal(await campo.isDisabled(), true, 'Janela encerrada não autoriza texto livre')
    const mensagensModeloAntes = await ler(`/api/atendimento/conversas/${fechada.id}/mensagens?limite=200`)
    const catalogoRota = '**/api/atendimento/modelos?*'
    let catalogosAbortados = 0, modelosEnviados = 0
    page.on('request', r => { if (r.method() === 'POST' && new URL(r.url()).pathname.endsWith('/mensagens/modelo')) modelosEnviados++ })
    await page.route(catalogoRota, route => { catalogosAbortados++; return route.abort('failed') })
    await page.getByRole('button', { name: 'Respostas', exact: true }).click()
    const modal = page.getByRole('dialog', { name: 'Enviar modelo aprovado', exact: true })
    await modal.getByRole('alert').first().waitFor()
    assert.equal(await modal.getByRole('button', { name: 'Enviar modelo', exact: true }).isDisabled(), true)
    assert.equal(catalogosAbortados, 1); assert.equal(modelosEnviados, 0)
    prova.falhaCatalogo = { mecanismo: 'Falha de transporte controlada exclusivamente no GET real /api/atendimento/modelos', mensagem: await modal.getByRole('alert').allTextContents() }
    await page.screenshot({ path: resolve(pasta, 'modelo-catalogo-indisponivel.png'), fullPage: true })
    registrar('Janela real encerrada bloqueia texto; catálogo indisponível informa erro sem autorizar modelo', prova.falhaCatalogo)
    await page.unroute(catalogoRota)
    const consultando = page.waitForResponse(r => new URL(r.url()).pathname === '/api/atendimento/modelos' && r.request().method() === 'GET')
    await modal.getByRole('button', { name: 'Atualizar modelos', exact: true }).click()
    const r = await consultando; const catalogo = await r.json()
    prova.catalogo = { status: r.status(), codigo: catalogo.error?.code, quantidade: catalogo.data?.length }
    if (!r.ok() || catalogo.data?.length === 0) {
      if (!r.ok()) await modal.getByRole('alert').first().waitFor()
      else await modal.getByText('Nenhum modelo aprovado nesta conta. Crie e aprove o modelo no Gerenciador da Meta.', { exact: true }).waitFor()
      assert.equal(await modal.getByRole('button', { name: 'Enviar modelo', exact: true }).isDisabled(), true)
      registrar('Atualização real do catálogo mantém indisponibilidade/vazio explícitos', prova.catalogo)
    } else {
      const indisponivel = catalogo.data.findIndex(m => m.indisponivel)
      if (indisponivel >= 0) {
        await modal.getByLabel('Modelo aprovado', { exact: true }).selectOption(String(indisponivel))
        await modal.getByText(catalogo.data[indisponivel].indisponivel, { exact: true }).waitFor()
        assert.equal(await modal.getByRole('button', { name: 'Enviar modelo', exact: true }).isDisabled(), true)
        registrar('Modelo listado mas indisponível no canal permanece bloqueado')
      }
      prova.limitacaoModelos = 'Nenhum envio de modelo foi executado; falha de catálogo é controlada e atualização usa API real. Publicação editorial tem prova separada e não autoriza canal.'
    }
    assert.equal(modelosEnviados, 0)
    assert.deepEqual(await ler(`/api/atendimento/conversas/${fechada.id}/mensagens?limite=200`), mensagensModeloAntes)
    await page.screenshot({ path: resolve(pasta, 'modelo-catalogo-atualizado.png'), fullPage: true })
  } else prova.limitacaoModelos = 'RED sem conversa separada para canal; somente intermediários do envio de texto exercitados.'
  prova.versao.final = await hashesServidos()
  assert.deepEqual(prova.versao.final, prova.versao.inicial, 'A fonte servida deve permanecer congelada durante a rodada')
  assert.deepEqual(prova.erros, [])
  assert.deepEqual(prova.violacoes, [], 'Cada intenção deve ter um balão em todos os estados intermediários')
  prova.sucesso = true
  salvar()
} catch (erro) {
  prova.sucesso = false
  if (prova.versao.inicial) {
    prova.versao.final = await hashesServidos().catch(() => null)
    prova.versao.fonteEstavel = JSON.stringify(prova.versao.inicial) === JSON.stringify(prova.versao.final)
  }
  prova.falha = { etapa, mensagem: erro.message }
  salvar()
  await page.screenshot({ path: resolve(pasta, 'mensagem-falha.png'), fullPage: true }).catch(() => {})
  throw erro
} finally {
  liberarAck()
  await browser.close()
}
