// Opt-in: App, JWT e PostgreSQL da fixture R4. Somente duas contas sintéticas; nenhuma inferência ou pedido.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const origem = process.env.CONSOLE_TEST_URL
assert.ok(origem, 'Informe CONSOLE_TEST_URL após liberação da fixture R4.')
const fixture = JSON.parse(readFileSync(process.env.R4_FIXTURE ?? '../.build/r4-browser.json', 'utf8'))
const dona = fixture.identidades.find(i => i.empresaId === fixture.empresaId && i.perfil === 'Dona')
assert.ok(dona)
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2395/desfazer-conta-real')
mkdirSync(pasta, { recursive: true })
const prefixo = `Regressão R4 desfazer ${Date.now().toString(36)}`
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, locale: 'pt-BR', reducedMotion: 'reduce' })
const a = await contexto.newPage(), b = await contexto.newPage(), erros = [], patches = []
let etapa = 'login'
for (const p of [a, b]) {
  p.setDefaultTimeout(20000); p.on('pageerror', e => erros.push(e.message))
  p.on('response', r => { if (r.request().method() === 'PATCH' && new URL(r.url()).pathname.startsWith('/api/cadastros/contas-bancarias/')) patches.push({ path: new URL(r.url()).pathname, status: r.status(), dados: r.request().postDataJSON() }) })
}
const caminho = id => `/api/cadastros/contas-bancarias${id ? `/${id}` : ''}`
async function login(p) {
  await p.goto(`${origem}/#/m/cadastros/bancos`)
  await p.getByRole('textbox', { name: 'E-mail', exact: true }).fill(dona.email)
  await p.getByLabel('Senha', { exact: true }).fill(dona.senha)
  await p.getByRole('button', { name: 'Entrar', exact: true }).tap()
  await p.getByRole('button', { name: 'Novo cadastro', exact: true }).waitFor()
}
async function resposta(p, path, metodo, acao, status = 200) {
  const pendente = p.waitForResponse(r => new URL(r.url()).pathname === path && r.request().method() === metodo)
  await acao()
  const r = await pendente
  assert.equal(r.status(), status, `${etapa}: ${metodo} ${path}`)
  return status === 200 ? (await r.json()).data : null
}
async function criar(nome) {
  await a.getByRole('button', { name: 'Novo cadastro', exact: true }).tap()
  const modal = a.getByRole('dialog', { name: 'Novo cadastro bancário', exact: true })
  for (const [rotulo, valor] of [['Nome da conta', nome], ['Instituição financeira', 'Original R4'], ['Número da conta', '000123-0'], ['Titular', 'Titular sintético R4']]) await modal.getByRole('textbox', { name: rotulo, exact: true }).fill(valor)
  const conta = await resposta(a, caminho(), 'POST', () => modal.getByRole('button', { name: 'Salvar cadastro', exact: true }).tap())
  await modal.waitFor({ state: 'hidden' }); return conta
}
async function editar(p, conta, valor) {
  await p.getByRole('button', { name: `Editar ${conta.nome}`, exact: true }).tap()
  const modal = p.getByRole('dialog', { name: 'Editar cadastro bancário', exact: true })
  await modal.getByRole('textbox', { name: 'Instituição financeira', exact: true }).fill(valor)
  const atual = await resposta(p, caminho(conta.id), 'PATCH', () => modal.getByRole('button', { name: 'Salvar cadastro', exact: true }).tap())
  await modal.waitFor({ state: 'hidden' }); return atual
}
async function ler(id) {
  return a.evaluate(async path => {
    const sessao = JSON.parse(sessionStorage.getItem('easystok.sessao') ?? localStorage.getItem('easystok.sessao'))
    const r = await fetch(path, { headers: { Authorization: `Bearer ${sessao.token}` } })
    if (!r.ok) throw new Error(`Leitura ${r.status}`)
    return (await r.json()).data
  }, caminho(id))
}
try {
  await login(a)
  const contaA = await criar(`${prefixo} A`), contaB = await criar(`${prefixo} B`)
  await login(b)
  etapa = 'B posterior preservada'
  const propria = await editar(a, contaA, 'Edição A')
  await editar(b, contaB, 'B posterior em outra aba')
  const posteriorB = await ler(contaB.id)
  const revertida = await resposta(a, caminho(contaA.id), 'PATCH', () => a.getByRole('button', { name: 'Desfazer edição desta conta', exact: true }).tap())
  await a.getByRole('status').filter({ hasText: 'Edição desfeita somente' }).waitFor()
  assert.equal((await ler(contaA.id)).instituicao, contaA.instituicao)
  assert.deepEqual(await ler(contaB.id), posteriorB, 'O registro B posterior permanece integralmente igual')
  assert.equal(revertida.revisao, propria.revisao + 1)
  assert.notEqual(revertida.ultimaOperacaoId, propria.ultimaOperacaoId)
  assert.equal(revertida.alteradoPor, propria.alteradoPor)
  assert.equal(patches.at(-1).dados.revisao, propria.revisao)
  assert.notEqual(patches.at(-1).dados.requestId, patches[0].dados.requestId)
  await a.screenshot({ path: resolve(pasta, 'C02-B-preservada-390.png'), fullPage: true })
  etapa = 'A posterior recusa409'
  const antesConflito = await editar(a, contaA, 'Proposta antes de terceiro')
  await b.reload()
  await editar(b, contaA, 'A posterior em outra aba')
  const posteriorA = await ler(contaA.id)
  await resposta(a, caminho(contaA.id), 'PATCH', () => a.getByRole('button', { name: 'Desfazer edição desta conta', exact: true }).tap(), 409)
  await a.getByRole('alert').filter({ hasText: 'A conta mudou depois desta edição' }).waitFor()
  assert.deepEqual(await ler(contaA.id), posteriorA)
  assert.deepEqual(await ler(contaB.id), posteriorB)
  assert.equal(patches.at(-1).dados.revisao, antesConflito.revisao)
  await a.screenshot({ path: resolve(pasta, 'C02-A-posterior-409-390.png'), fullPage: true })
  await a.reload()
  assert.deepEqual(await ler(contaA.id), posteriorA)
  assert.deepEqual(await ler(contaB.id), posteriorB)
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ appApiJwtPostgreSqlReais: true, empresaId: fixture.empresaId, prefixo, ids: [contaA.id, contaB.id], duasAbas: true, escritaBPreservada: true, escritaAPosteriorRecusada409: true, persistenciaAposReload: true, alteracaoRegistrada: { revisao: revertida.revisao, operacaoId: revertida.ultimaOperacaoId, ator: revertida.alteradoPor }, patches }, null, 2))
  console.log('C02 real: duas abas390, Desfazer só A preserva B; operação/revisão/ator registrados; A posterior409, persistência após reload.')
} catch (erro) { await a.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(etapa, erros.join('\n')); throw erro }
finally { await contexto.close(); await browser.close() }
