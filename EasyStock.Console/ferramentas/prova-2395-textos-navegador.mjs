// R03: limites canônicos e preservação integral na UI real, sem enviar mensagem ou gerar pedido.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'

assert.equal(process.env.R4_EXECUTAR_TEXTOS, '1', 'A execução exige liberação da fixture e R4_EXECUTAR_TEXTOS=1')
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const arquivoFixture = process.env.R4_FIXTURE ?? '../.build/r4-browser.json'
const fixture = JSON.parse(readFileSync(arquivoFixture, 'utf8'))
const origem = process.env.CONSOLE_TEST_URL
const conversaId = process.env.R4_CONVERSA_TEXTOS ?? fixture.conversaId
for (const url of [origem, fixture.api]) assert.ok(['localhost', '127.0.0.1'].includes(new URL(url).hostname))
assert.equal(conversaId, fixture.conversaId, 'Esta prova usa somente Ana principal, com autorização explícita')
const hash = texto => createHash('sha256').update(texto).digest('hex')
const retrato = texto => ({ comprimento: texto.length, sha256: hash(texto), final: texto.slice(-60) })
const fonteResposta = readFileSync('src/dominio/limiteDoTexto.js', 'utf8')
const fonteNota = readFileSync('../EasyStock.Domain/Entities/ClienteNota.cs', 'utf8')
const limiteResposta = Number(fonteResposta.match(/LIMITE_TEXTO_MENSAGEM = (\d+)/)[1])
const limiteNota = Number(fonteNota.match(/TextoTamanhoMaximo = (\d+)/)[1])
const pasta = resolve(process.env.R4_SAIDA_TEXTOS ?? '.build/2395/real/r03-textos')
mkdirSync(pasta, { recursive: true })
const prova = { conversaId, fixtureSha256: hash(readFileSync(arquivoFixture)), versaoApi: fixture.versaoApi ?? null,
  limites: { resposta: limiteResposta, notaInterna: limiteNota, observacaoItem: 'Sem limite finito declarado; coluna PostgreSQL text e normalização Trim' },
  inicio: new Date().toISOString(), resultados: [], requisicoesNota: [], erros: [] }
const salvar = () => writeFileSync(resolve(pasta, 'textos-ui.json'), JSON.stringify(prova, null, 2))
const registrar = dado => { prova.resultados.push(dado); salvar(); console.log(`ok ${dado.campo} ${dado.original.comprimento}`) }
const textoDe = (campo, n) => { const inicio = `R03 sintético ${campo} ${n}: `, fim = ` FIM-R03-${campo}-${n}`; return inicio + 'x'.repeat(n - inicio.length - fim.length) + fim }
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, locale: 'pt-BR', reducedMotion: 'reduce' })
const page = await context.newPage()
page.setDefaultTimeout(20000)
page.on('pageerror', erro => prova.erros.push(erro.message))
let etapa = 'login', conversa
const mensagens = () => page.getByRole('textbox', { name: 'Mensagem para o cliente', exact: true })
async function ler(rota) {
  return page.evaluate(async rota => {
    const sessao = JSON.parse(sessionStorage.getItem('easystok.sessao') ?? localStorage.getItem('easystok.sessao'))
    const r = await fetch(rota, { headers: { Authorization: `Bearer ${sessao.token}` } })
    if (!r.ok) throw new Error(`GET ${rota}: ${r.status}`)
    return (await r.json()).data
  }, rota)
}
async function selecionar() {
  const todas = page.getByRole('button', { name: /^Todas\b/ })
  await todas.waitFor(); await todas.click()
  await page.getByPlaceholder('Buscar conversa', { exact: true }).fill(`comanda ${conversa.numeroComanda}`)
  await page.getByRole('button', { name: new RegExp(`Comanda ${conversa.numeroComanda},`) }).click()
  await mensagens().waitFor()
}
async function navegarEVoltar() {
  await page.locator('a[href="#/"]:visible').first().click()
  await page.locator('a[href="#/m/atendimento"]:visible').first().click()
  await selecionar()
}
async function recarregar() { await page.reload(); await selecionar() }
async function abrirNota() {
  const aberto = page.getByRole('dialog', { name: /^Nota interna de / })
  if (!await aberto.isVisible()) await page.getByRole('button', { name: /^(Retomar nota interna|Nota interna)$/ }).click()
  const campo = page.getByRole('textbox', { name: /^Texto da nota de / })
  await campo.waitFor(); return campo
}
async function recolherNota() {
  const dialogo = page.getByRole('dialog', { name: /^Nota interna de / })
  if (await dialogo.isVisible()) await dialogo.getByRole('button', { name: /^(Recolher|Conversa)$/ }).click()
}
async function textoDaObs() {
  const nomeProduto = fixture.produtos[0].nome.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const botao = page.getByRole('button', { name: new RegExp(`^Anotação para ${nomeProduto}$`, 'i') })
  if (!await botao.isVisible()) {
    const revisar = page.getByRole('button', { name: /^Revisar comanda/ }).first()
    if (await revisar.isVisible()) await revisar.click()
  }
  const campo = page.getByRole('textbox', { name: new RegExp(`^Observação de ${nomeProduto}$`, 'i') })
  if (!await campo.isVisible()) await botao.click()
  await campo.waitFor(); return campo
}
try {
  const dona = fixture.identidades.find(i => i.perfil === 'Dona' && i.empresaId === fixture.empresaId)
  assert.ok(dona)
  await page.goto(origem + '/#/m/atendimento/balcao')
  await page.getByLabel('E-mail', { exact: true }).fill(dona.email)
  await page.getByLabel('Senha', { exact: true }).fill(dona.senha)
  const autenticando = page.waitForResponse(r => new URL(r.url()).pathname === '/api/auth/login' && r.request().method() === 'POST')
  await page.getByRole('button', { name: 'Entrar', exact: true }).click()
  assert.equal((await autenticando).status(), 200)
  await page.getByRole('button', { name: /^Todas\b/ }).waitFor()
  conversa = (await ler('/api/atendimento/conversas?limite=200')).find(c => c.id === conversaId)
  assert.ok(conversa?.clienteId && !conversa.vinculoEmRevisao)
  await selecionar()
  const rotaNotas = `/api/clientes/${conversa.clienteId}/notas`
  const notas = async () => (await ler(`/api/atendimento/conversas/${conversaId}/dossie`)).notas
  const notasAntes = await notas()
  const mensagensAntes = await ler(`/api/atendimento/conversas/${conversaId}/mensagens?limite=200`)
  const pedidoAntes = await ler(`/api/atendimento/conversas/${conversaId}/pedido`)
  prova.destino = { clienteId: conversa.clienteId, nome: conversa.contatoNome, numeroComanda: conversa.numeroComanda }
  for (const modulo of ['src/dominio/limiteDoTexto.js', 'src/features/notas/ModalNota.jsx', 'src/infra/api/visitaDaSessao.js']) {
    const servido = await page.request.get(`${origem}/${modulo}`); assert.ok(servido.ok())
    ;(prova.fontes ??= {})[modulo] = { servidoSha256: hash(await servido.body()), fonteSha256: hash(readFileSync(modulo)) }
  }
  for (const n of [limiteResposta - 1, limiteResposta, limiteResposta + 1, 4001]) {
    etapa = `resposta-${n}`
    const texto = textoDe('resposta', n)
    await mensagens().fill(texto); await mensagens().press('Tab')
    const bloqueado = await page.getByRole('button', { name: 'Enviar mensagem', exact: true }).isDisabled()
    assert.equal(bloqueado, n > limiteResposta)
    const aviso = n > limiteResposta
      ? await page.getByRole('alert').filter({ hasText: /A mensagem passa .* do limite/ }).innerText()
      : await page.getByText(/Ainda cabem .*limite de/).first().innerText()
    const digitado = await mensagens().inputValue(); assert.equal(digitado, texto)
    await navegarEVoltar()
    const navegado = await mensagens().inputValue(); assert.equal(navegado, texto)
    await recarregar()
    const recarregado = await mensagens().inputValue(); assert.equal(recarregado, texto)
    await page.screenshot({ path: resolve(pasta, `${etapa}.png`) })
    registrar({ campo: 'resposta', original: retrato(texto), digitado: retrato(digitado), navegado: retrato(navegado), recarregado: retrato(recarregado), envioBloqueado: bloqueado, aviso, persistencia: 'Rascunho da aba, sem envio' })
  }
  await mensagens().fill(''); await mensagens().press('Tab')
  for (const n of [limiteNota - 1, limiteNota, limiteNota + 1, 4001]) {
    etapa = `nota-${n}`
    const texto = textoDe('nota', n), campo = await abrirNota()
    await campo.fill(texto)
    const resposta = page.waitForResponse(r => new URL(r.url()).pathname === rotaNotas && r.request().method() === 'POST')
    await page.getByRole('dialog', { name: /^Nota interna de / }).getByRole('button', { name: /^(Fixar nota|Salvar)$/ }).click()
    const http = await resposta, corpo = await http.json()
    prova.requisicoesNota.push({ tamanho: n, status: http.status(), erro: corpo.error ?? null })
    if (n <= limiteNota) {
      assert.ok(http.ok(), JSON.stringify(corpo))
      const gravada = (await notas()).find(nota => nota.texto === texto); assert.ok(gravada)
      await navegarEVoltar(); await recarregar()
      const confirmada = (await notas()).find(nota => nota.id === gravada.id); assert.equal(confirmada?.texto, texto)
      registrar({ campo: 'nota', original: retrato(texto), persistida: retrato(gravada.texto), recarregada: retrato(confirmada.texto), notaId: gravada.id, status: http.status() })
    } else {
      assert.equal(http.status(), 400)
      await page.getByRole('dialog', { name: /^Nota interna de / }).getByRole('alert').waitFor()
      const alertas = await page.getByRole('alert').allTextContents()
      assert.ok(alertas.some(a => /Sem confirmação|500 caracteres/.test(a)))
      assert.equal(await campo.inputValue(), texto)
      await recolherNota(); await navegarEVoltar()
      const navegado = await (await abrirNota()).inputValue(); assert.equal(navegado, texto)
      await recolherNota(); await recarregar()
      const recarregado = await (await abrirNota()).inputValue(); assert.equal(recarregado, texto)
      assert.ok(!(await notas()).some(nota => nota.texto === texto))
      await page.screenshot({ path: resolve(pasta, `${etapa}.png`) })
      registrar({ campo: 'nota', original: retrato(texto), navegado: retrato(navegado), recarregado: retrato(recarregado), status: http.status(), alertas, persistencia: 'Recusada pela API; original recuperado na mesma aba' })
    }
  }
  await recolherNota()
  etapa = 'observacao-montagem'
  assert.equal(pedidoAntes, null, 'Observação adicional exige uma comanda local sem pedido')
  await page.getByRole('button', { name: 'Adicionar itens', exact: true }).click()
  await page.getByRole('button', { name: new RegExp(`^(Somar )?${fixture.produtos[0].nome}`, 'i') }).first().click()
  const fecharCardapio = page.getByRole('button', { name: /^Fechar cardápio$/ })
  if (await fecharCardapio.isVisible()) await fecharCardapio.click()
  for (const n of [3999, 4000, 4001]) {
    etapa = `observacao-${n}`
    const texto = textoDe('observacao', n), campo = await textoDaObs()
    await campo.fill(texto); await campo.press('Tab')
    await navegarEVoltar()
    const navegado = await (await textoDaObs()).inputValue(); assert.equal(navegado, texto)
    await recarregar()
    const recarregado = await (await textoDaObs()).inputValue(); assert.equal(recarregado, texto)
    await page.screenshot({ path: resolve(pasta, `${etapa}.png`) })
    registrar({ campo: 'observacao-item', original: retrato(texto), navegado: retrato(navegado), recarregado: retrato(recarregado), persistencia: 'Comanda local da aba, sem criar pedido' })
  }
  assert.deepEqual((await ler(`/api/atendimento/conversas/${conversaId}/mensagens?limite=200`)).map(m => m.id), mensagensAntes.map(m => m.id))
  assert.equal(await ler(`/api/atendimento/conversas/${conversaId}/pedido`), pedidoAntes)
  assert.equal((await notas()).length, notasAntes.length + 2)
  assert.deepEqual(prova.erros, [])
  prova.status = 'passou'
} catch (erro) {
  prova.status = 'falhou'; prova.etapa = etapa; prova.falha = erro.stack
  await page.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }).catch(() => {})
  process.exitCode = 1
} finally { prova.fim = new Date().toISOString(); salvar(); await browser.close() }
console.log(JSON.stringify({ status: prova.status, etapa: prova.etapa, casos: prova.resultados.length, arquivo: resolve(pasta, 'textos-ui.json') }))
