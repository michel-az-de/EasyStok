// Opt-in: UI/HTTP/JWT/PostgreSQL reais da fixture R4. Nenhum pedido, mensagem ou LLM.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_TEST_URL
assert.ok(origem, 'Informe CONSOLE_TEST_URL após a fixture R4 estar pronta.')
const fixture = JSON.parse(readFileSync(process.env.R4_FIXTURE ?? '../.build/r4-browser.json', 'utf8'))
const dona = fixture.identidades.find(i => i.empresaId === fixture.empresaId && i.perfil === 'Dona')
const atendente = fixture.identidades.find(i => i.empresaId === fixture.empresaId && i.perfil === 'Atendimento')
assert.ok(dona && atendente, 'A fixture deve fornecer Dona e Atendimento.')
const execucao = Date.now().toString(36)
const prefixo = `Regressão R4 ${execucao}`
const pasta = resolve(import.meta.dirname, '../.build/2395/concorrencia')
mkdirSync(pasta, { recursive: true })
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'pt-BR', reducedMotion: 'reduce' })
const pagina = await contexto.newPage()
const outra = await contexto.newPage()
const resultados = [], erros = [], chamadas = []
let etapa = 'login'
let paginaAtiva = pagina
for (const p of [pagina, outra]) {
  p.setDefaultTimeout(20000)
  p.on('pageerror', e => erros.push(e.message))
  p.on('response', r => {
    const path = new URL(r.url()).pathname
    if (path.startsWith('/api/cadastros/') || path.startsWith('/api/notificacoes/'))
      chamadas.push({ path, metodo: r.request().method(), status: r.status() })
  })
}
const registro = (caso, dados = {}) => { resultados.push({ caso, ...dados }); console.log(`ok ${caso}`) }
const rutaBancos = '#/m/cadastros/bancos'
const bancoPath = id => `/api/cadastros/contas-bancarias${id ? `/${id}` : ''}`
async function login(p, identidade, rota = rutaBancos) {
  await p.goto(`${origem}/${rota}`)
  const email = p.getByRole('textbox', { name: 'E-mail', exact: true })
  await Promise.any([email.waitFor(), p.getByRole('button', { name: 'Novo cadastro', exact: true }).waitFor(),
    p.getByText('Esta área precisa de outro acesso', { exact: true }).waitFor()])
  if (!await email.isVisible()) return
  await email.fill(identidade.email)
  await p.getByLabel('Senha', { exact: true }).fill(identidade.senha)
  await p.getByRole('button', { name: 'Entrar', exact: true }).click()
  await email.waitFor({ state: 'hidden' })
}
// Leitura de reconciliação; a única escrita direta é uma tentativa negada com perfil sem permissão.
async function api(p, caminho, metodo = 'GET', corpo = undefined) {
  return p.evaluate(async ({ caminho, metodo, corpo }) => {
    const sessao = JSON.parse(sessionStorage.getItem('easystok.sessao') ?? localStorage.getItem('easystok.sessao'))
    const r = await fetch(caminho, { method: metodo, headers: {
      Authorization: `Bearer ${sessao.token}`, ...(corpo ? { 'Content-Type': 'application/json' } : {}),
    }, ...(corpo ? { body: JSON.stringify(corpo) } : {}) })
    const texto = await r.text()
    let json = null
    if (texto) { try { json = JSON.parse(texto) } catch { /* uma recusa pode não conter JSON */ } }
    return { status: r.status, json }
  }, { caminho, metodo, corpo })
}
async function lerBanco(id) {
  const r = await api(pagina, bancoPath(id)); assert.equal(r.status, 200)
  return r.json.data
}
async function salvar(p, dialogo, caminho, metodo, esperado = 200, botao = 'Salvar cadastro') {
  const esperando = p.waitForResponse(r => new URL(r.url()).pathname === caminho && r.request().method() === metodo)
  await dialogo.getByRole('button', { name: botao, exact: true }).click()
  const r = await esperando
  assert.equal(r.status(), esperado, `${etapa}: ${metodo} ${caminho}`)
  if (esperado === 200) await dialogo.waitFor({ state: 'hidden' })
  return r.json()
}
async function abrirBanco(p, nome) {
  await p.getByRole('button', { name: `Editar ${nome}`, exact: true }).click()
  const dialogo = p.getByRole('dialog', { name: 'Editar cadastro bancário', exact: true })
  await dialogo.waitFor()
  return dialogo
}
async function descartar(dialogo) {
  await dialogo.getByRole('button', { name: 'Cancelar', exact: true }).click()
  const descartar = dialogo.getByRole('button', { name: 'Descartar', exact: true })
  if (await descartar.isVisible()) await descartar.click()
  await dialogo.waitFor({ state: 'hidden' })
}
async function criarBanco(nome) {
  await pagina.getByRole('button', { name: 'Novo cadastro', exact: true }).click()
  const dialogo = pagina.getByRole('dialog', { name: 'Novo cadastro bancário', exact: true })
  await dialogo.getByRole('textbox', { name: 'Nome da conta', exact: true }).fill(nome)
  await dialogo.getByRole('textbox', { name: 'Instituição financeira', exact: true }).fill('Instituição sintética')
  await dialogo.getByRole('textbox', { name: 'Número da conta', exact: true }).fill('000123-0')
  await dialogo.getByRole('textbox', { name: 'Titular', exact: true }).fill('Titular sintético R4')
  const r = await salvar(pagina, dialogo, bancoPath(), 'POST')
  return r.data
}
try {
  await login(pagina, dona)
  const a = await criarBanco(`${prefixo} A`)
  const b = await criarBanco(`${prefixo} B`)
  // Nova aba possui sessionStorage próprio; autentica pela tela, sem copiar token do usuário.
  await login(outra, dona)

  etapa = 'C01-registros-diferentes'
  let dialogoA = await abrirBanco(pagina, a.nome)
  let dialogoB = await abrirBanco(outra, b.nome)
  await dialogoA.getByRole('textbox', { name: 'Titular', exact: true }).fill(`${prefixo} titular A`)
  await dialogoB.getByRole('textbox', { name: 'Titular', exact: true }).fill(`${prefixo} titular B`)
  await Promise.all([salvar(pagina, dialogoA, bancoPath(a.id), 'PATCH'), salvar(outra, dialogoB, bancoPath(b.id), 'PATCH')])
  await Promise.all([pagina.reload(), outra.reload()])
  assert.equal((await lerBanco(a.id)).titular, `${prefixo} titular A`)
  assert.equal((await lerBanco(b.id)).titular, `${prefixo} titular B`)
  registro('C01 duas abas preservam alterações de registros diferentes após recarga', { ids: [a.id, b.id] })

  etapa = 'C01-mesmo-registro'
  dialogoA = await abrirBanco(pagina, a.nome)
  dialogoB = await abrirBanco(outra, a.nome)
  const antes = await lerBanco(a.id)
  await dialogoA.getByRole('textbox', { name: 'Instituição financeira', exact: true }).fill('Instituição confirmada A')
  await dialogoB.getByRole('textbox', { name: 'Instituição financeira', exact: true }).fill('Proposta preservada B')
  await salvar(pagina, dialogoA, bancoPath(a.id), 'PATCH')
  await salvar(outra, dialogoB, bancoPath(a.id), 'PATCH', 409)
  await dialogoB.getByRole('alert').filter({ hasText: 'Este cadastro mudou.' }).waitFor()
  assert.equal(await dialogoB.getByRole('textbox', { name: 'Instituição financeira', exact: true }).inputValue(), 'Proposta preservada B')
  const depois = await lerBanco(a.id)
  assert.equal(depois.instituicao, 'Instituição confirmada A'); assert.equal(depois.revisao, antes.revisao + 1)
  await outra.screenshot({ path: resolve(pasta, 'C01-conflito.png'), fullPage: true })
  await descartar(dialogoB)
  registro('C01 revisão obsoleta recebe 409 e conserva a proposta do operador', { id: a.id, antes: antes.revisao, depois: depois.revisao })

  etapa = 'R03-limites'
  for (const tamanho of [149, 150, 151]) {
    dialogoA = await abrirBanco(pagina, a.nome)
    const texto = 'R'.repeat(tamanho - 1) + 'Z'
    await dialogoA.getByRole('textbox', { name: 'Instituição financeira', exact: true }).fill(texto)
    assert.equal(await dialogoA.getByRole('textbox', { name: 'Instituição financeira', exact: true }).inputValue(), texto)
    const previo = await lerBanco(a.id)
    await salvar(pagina, dialogoA, bancoPath(a.id), 'PATCH', tamanho <= 150 ? 200 : 400)
    const atual = await lerBanco(a.id)
    if (tamanho <= 150) assert.equal(atual.instituicao, texto)
    else {
      await dialogoA.getByRole('alert').waitFor()
      assert.equal(await dialogoA.getByRole('textbox', { name: 'Instituição financeira', exact: true }).inputValue(), texto)
      assert.equal(atual.instituicao, previo.instituicao); assert.equal(atual.revisao, previo.revisao)
      await pagina.screenshot({ path: resolve(pasta, 'R03-limite-preservado.png'), fullPage: true })
      await descartar(dialogoA)
    }
    registro(`R03 instituição ${tamanho} caracteres sem truncamento`, { id: a.id, status: tamanho <= 150 ? 200 : 400, persistido: atual.instituicao.length })
  }

  etapa = 'R04-rede'
  dialogoA = await abrirBanco(pagina, b.nome)
  const previoB = await lerBanco(b.id)
  await dialogoA.getByRole('textbox', { name: 'Titular', exact: true }).fill(`${prefixo} recuperação`)
  await contexto.setOffline(true)
  await dialogoA.getByRole('button', { name: 'Salvar cadastro', exact: true }).click()
  await dialogoA.getByRole('alert').waitFor()
  assert.equal(await dialogoA.getByRole('textbox', { name: 'Titular', exact: true }).inputValue(), `${prefixo} recuperação`)
  await contexto.setOffline(false)
  assert.equal((await lerBanco(b.id)).revisao, previoB.revisao)
  await salvar(pagina, dialogoA, bancoPath(b.id), 'PATCH')
  await pagina.reload()
  const recuperado = await lerBanco(b.id)
  assert.equal(recuperado.titular, `${prefixo} recuperação`); assert.equal(recuperado.revisao, previoB.revisao + 1)
  registro('R04 falha de rede preserva edição e recuperação grava uma única revisão', { id: b.id, revisao: recuperado.revisao })

  etapa = 'P01-permissao'
  const contextoRestrito = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'pt-BR' })
  const restrita = await contextoRestrito.newPage()
  paginaAtiva = restrita
  await login(restrita, atendente)
  await restrita.getByText(/^(Esta área precisa de outro acesso|Seu perfil não abre esta tela)$/).waitFor()
  assert.equal(await restrita.getByRole('button', { name: 'Novo cadastro', exact: true }).count(), 0)
  const lido = await api(restrita, bancoPath(a.id)); assert.equal(lido.status, 403)
  const base = await lerBanco(a.id)
  const negado = await api(restrita, bancoPath(a.id), 'PATCH', { ...base, requestId: crypto.randomUUID(), titular: 'Não deve persistir' })
  assert.equal(negado.status, 403); assert.equal((await lerBanco(a.id)).titular, base.titular)
  await restrita.screenshot({ path: resolve(pasta, 'P01-acesso-negado.png'), fullPage: true })
  await contextoRestrito.close()
  paginaAtiva = pagina
  registro('P01 link direto e escrita sem permissão recusados sem alterar cadastro', { id: a.id, leitura: lido.status, escrita: negado.status })

  etapa = 'M01-modelos'
  await pagina.goto(`${origem}/#/m/cadastros/modelos`)
  await pagina.getByRole('button', { name: 'E-mail e SMS', exact: true }).click()
  await pagina.getByRole('button', { name: 'Novo modelo', exact: true }).click()
  let modeloDialogo = pagina.getByRole('dialog', { name: 'Novo modelo', exact: true })
  const nomeModelo = `${prefixo} modelo`
  const codigo = `r4_regressao_${execucao}`
  await modeloDialogo.getByRole('textbox', { name: 'Nome do modelo', exact: true }).fill(nomeModelo)
  await modeloDialogo.getByRole('textbox', { name: 'Identificação do modelo', exact: true }).fill(codigo)
  await modeloDialogo.getByRole('textbox', { name: 'Assunto', exact: true }).fill('Regressão R4 primeira versão')
  await modeloDialogo.getByRole('textbox', { name: 'Mensagem', exact: true }).fill('Regressão R4 conteúdo original sem destinatário.')
  await modeloDialogo.getByRole('button', { name: 'Ver prévia', exact: true }).click()
  await modeloDialogo.getByRole('heading', { name: 'Prévia sem envio', exact: true }).waitFor()
  const rascunho = (await salvar(pagina, modeloDialogo, '/api/notificacoes/templates', 'POST', 200, 'Salvar modelo')).data
  let item = pagina.getByRole('listitem').filter({ has: pagina.getByRole('heading', { name: nomeModelo, exact: true }) })
  await item.getByText('Aguardando revisão', { exact: true }).waitFor()
  async function aprovar() {
    const resposta = pagina.waitForResponse(r => new URL(r.url()).pathname.endsWith('/aprovar') && r.request().method() === 'POST')
    await item.getByRole('button', { name: 'Aprovar na loja', exact: true }).click()
    assert.equal((await resposta).status(), 200)
    await item.getByText('Aprovado na loja', { exact: true }).waitFor()
  }
  await aprovar()
  const primeiro = await api(pagina, `/api/notificacoes/templates/${rascunho.id}`)
  assert.equal(primeiro.json.data.ativo, true)
  await item.getByRole('button', { name: 'Editar', exact: true }).click()
  modeloDialogo = pagina.getByRole('dialog', { name: 'Editar modelo', exact: true })
  await modeloDialogo.getByRole('textbox', { name: 'Mensagem', exact: true }).fill('Regressão R4 conteúdo revisado sem destinatário.')
  const revisado = (await salvar(pagina, modeloDialogo, `/api/notificacoes/templates/${rascunho.id}`, 'PUT', 200, 'Salvar modelo')).data
  assert.equal((await api(pagina, `/api/notificacoes/templates/${rascunho.id}`)).json.data.ativo, true)
  assert.equal((await api(pagina, `/api/notificacoes/templates/${revisado.id}`)).json.data.ativo, false)
  const atuais = await api(pagina, '/api/notificacoes/templates?apenasAtuais=true&pageSize=200')
  assert.deepEqual(atuais.json.data.filter(m => m.codigo === codigo).map(m => m.id), [revisado.id])
  item = pagina.getByRole('listitem').filter({ has: pagina.getByRole('heading', { name: nomeModelo, exact: true }) })
  await item.getByText('Aguardando revisão', { exact: true }).waitFor()
  await aprovar()
  await pagina.reload()
  await pagina.getByRole('button', { name: 'E-mail e SMS', exact: true }).click()
  await pagina.getByRole('listitem').filter({ has: pagina.getByRole('heading', { name: nomeModelo, exact: true }) })
    .getByText('Aprovado na loja', { exact: true }).waitFor()
  const antigo = (await api(pagina, `/api/notificacoes/templates/${rascunho.id}`)).json.data
  const atual = (await api(pagina, `/api/notificacoes/templates/${revisado.id}`)).json.data
  assert.equal(antigo.ativo, false); assert.equal(atual.ativo, true); assert.equal(atual.aprovado, true)
  assert.equal(atual.corpoTemplate, 'Regressão R4 conteúdo revisado sem destinatário.')
  await pagina.screenshot({ path: resolve(pasta, 'M01-modelo-publicado.png'), fullPage: true })
  registro('M01 prévia sem envio, rascunho e troca de publicação persistem após recarga', { anterior: antigo.id, atual: atual.id, versao: atual.versao })
  etapa = 'concluida'
} catch (e) {
  resultados.push({ falha: etapa, mensagem: e.message })
  await paginaAtiva.screenshot({ path: resolve(pasta, `falha-${etapa}.png`), fullPage: true }).catch(() => {})
  const texto = await paginaAtiva.locator('body').innerText().catch(() => '')
  writeFileSync(resolve(pasta, `falha-${etapa}.txt`), texto.slice(0, 12000))
  console.error(`${etapa}: ${e.message}`)
  process.exitCode = 1
} finally {
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ escopo: 'UI, JWT, API e PostgreSQL da fixture reais; somente registros prefixados; nenhuma mensagem, cobrança, pedido ou inferência', prefixo, etapa, resultados, erros, chamadas }, null, 2))
  await browser.close()
}
