// #3149: Chromium, o PainelAssistente real (aba Dicionário) e HTTP controlado (não usa a API real).
// Vazio; acrescentar, editar e tirar termos; repetido por maiúscula ou acento trava o salvar; salvar manda só
// `dicionario`, aparado, e a aba relê; a mensagem do 400 aparece; quem não edita não faz nenhum GET da configuração.
// Fotos em 1280 e 375 px, sem rolagem lateral. As regras de domínio ficam em prova-3149-dicionario.mjs.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3149/dicionario')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {PainelAssistente} from '../../../src/features/gestao/atendimento/PainelAssistente';
import '../../../src/estilos/base.css';
const editar = new URLSearchParams(location.search).get('editar') !== '0';
for (const el of [document.documentElement, document.getElementById('root')]) el.style.cssText='height:auto;overflow:visible';
document.body.style.cssText='height:auto;overflow:visible;margin:0;padding:0 16px;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
createRoot(document.getElementById('root')).render(<ContextoAcessoModulos.Provider value={{acoes:{editarAtendimento:editar},permite:()=>true}}><PainelAssistente podeEditar={editar}/></ContextoAcessoModulos.Provider>);
`)

let dicionario = []
let falhaNoProximoPut = null
let seguraPut = null, seguraGet = null
const uso = { ano: 2026, mes: 11, turnos: 0, tokensEntrada: 0, tokensSaida: 0, tokensCacheLido: 0, totalTokens: 0, custoEstimado: 0, turnosSemPreco: 0,
  cotaTokensMes: null, percentual: null, alerta: false, respostasSemIa: 0, tokensPoupadosEstimados: 0 }
const fluxo = { ativo: true, modeloDisponivel: true, portoes: [], roteiro: [], roteiroPersonalizado: false, ferramentas: [], prompt: 'Você é o assistente da loja.', observacoesLoja: '' }

const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 900 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'admin' }, expiraEm: Date.now() + 600000 })))
const pedidos = [], erros = []
await contexto.route(url => url.pathname.startsWith('/api/'), async route => {
  const req = route.request(), path = new URL(req.url()).pathname
  const corpo = req.postDataJSON?.() ?? null
  pedidos.push({ metodo: req.method(), path, corpo })
  if (req.method() === 'GET' && path === '/api/atendimento/assistente/uso') return route.fulfill({ json: { data: uso } })
  if (req.method() === 'GET' && path === '/api/atendimento/assistente/fluxo') return route.fulfill({ json: { data: fluxo } })
  if (req.method() === 'GET' && path === '/api/atendimento/configuracao') { if (seguraGet) await seguraGet; return route.fulfill({ json: { data: { dicionario } } }) }
  if (req.method() === 'PUT' && path === '/api/atendimento/configuracao') {
    if (seguraPut) await seguraPut
    if (falhaNoProximoPut) { const mensagem = falhaNoProximoPut; falhaNoProximoPut = null; return route.fulfill({ status: 400, json: { error: { message: mensagem } } }) }
    if ('dicionario' in corpo) dicionario = corpo.dicionario
    return route.fulfill({ json: { data: { dicionario } } })
  }
  return route.fulfill({ status: 404, json: { error: { message: 'fora da prova: ' + path } } })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const endereco = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/3149/dicionario/index.html`
const putsDesde = (i) => pedidos.slice(i).filter(p => p.metodo === 'PUT').map(p => p.corpo)
const leiturasDesde = (i) => pedidos.slice(i).filter(p => p.metodo === 'GET' && p.path === '/api/atendimento/configuracao').length
const semRolagemLateral = async () => pagina.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
// Cada item da revisão confere sem parar a prova: as falhas saem juntas no fim.
const falhas = []
const conferir = async (nome, fn) => { try { await fn() } catch (e) { falhas.push(`${nome}: ${String(e.message).split('\n')[0]}`) } }
const focoNoBody = () => pagina.evaluate(() => document.activeElement === document.body || document.activeElement === null)
try {
  await pagina.goto(endereco)
  const painel = pagina.getByRole('region', { name: 'Painel do Assistente' })
  await painel.waitFor()
  assert.deepEqual((await painel.getByRole('tab').allInnerTexts()).map(t => t.trim()), ['Uso e cota', 'Fluxo', 'Histórico', 'Dicionário'])
  assert.equal(leiturasDesde(0), 0, 'a configuração só é lida quando a aba abre')

  // (a) Estado vazio.
  let liberarGet
  seguraGet = new Promise((r) => { liberarGet = r })
  await painel.getByRole('tab', { name: 'Dicionário' }).click()
  await conferir('item 6: aria-busy na leitura do dicionário', async () => {
    assert.equal(await pagina.locator('#painel-assistente-conteudo').getAttribute('aria-busy'), 'true')
  })
  seguraGet = null; liberarGet()
  await painel.getByText('Nenhum termo ainda. Acrescente o primeiro.').waitFor()
  await painel.getByText(/prato feito do dia/).first().waitFor()
  await painel.getByText('0 de 60 termos', { exact: true }).waitFor()
  assert.equal(leiturasDesde(0), 1)
  assert.equal(await painel.getByText(/Não consegui ler o Assistente/).count(), 0, 'sem o erro geral do painel nesta aba')
  assert.equal(await painel.getByRole('button', { name: 'Desfazer' }).count(), 0, 'Desfazer só com mudança')
  await pagina.screenshot({ path: resolve(pasta, 'vazio-1280.png'), fullPage: true })

  // (b) Acrescentar duas linhas, editar e tirar uma. O foco vai para o campo novo.
  const termos = painel.getByRole('textbox', { name: 'Termo', exact: true })
  const significados = painel.getByRole('textbox', { name: 'O que quer dizer', exact: true })
  await painel.getByRole('button', { name: 'Acrescentar termo' }).click()
  await termos.nth(0).waitFor()
  assert.equal(await termos.nth(0).evaluate(el => el === document.activeElement), true, 'foco no termo novo')
  await conferir('item 2: linha vazia recém-acrescentada sem alerta', async () => {
    assert.equal(await painel.getByRole('alert').count(), 0)
    assert.equal(await painel.getByRole('button', { name: 'Salvar dicionário' }).isDisabled(), true)
  })
  await termos.nth(0).fill('  PF ')
  await significados.nth(0).fill('prato feito')
  await painel.getByRole('button', { name: 'Acrescentar termo' }).click()
  assert.equal(await termos.nth(1).evaluate(el => el === document.activeElement), true, 'foco no segundo termo novo')
  await termos.nth(1).fill('Açaí')
  await significados.nth(1).fill(' fruta roxa batida ')
  await significados.nth(0).fill(' prato feito do dia ')
  await painel.getByText('2 de 60 termos', { exact: true }).waitFor()
  await painel.getByText('Dicionário com mudanças não salvas.', { exact: true }).waitFor()
  await painel.getByRole('button', { name: 'Acrescentar termo' }).click()
  await termos.nth(2).fill('Marmita')
  await conferir('item 2: linha incompleta vira nota comum', async () => { await painel.getByText('Preencha o termo e o que ele quer dizer em todas as linhas para salvar.').waitFor({ timeout: 1500 }) })
  assert.equal(await painel.getByRole('button', { name: 'Salvar dicionário' }).isDisabled(), true, 'sem significado não salva')
  await conferir('item 2: linha incompleta sem alerta', async () => { assert.equal(await painel.getByRole('alert').count(), 0) })
  await painel.getByRole('button', { name: 'Tirar o termo Marmita' }).click()
  assert.equal(await termos.count(), 2, 'a linha saiu')
  await conferir('item 3: tirar a última linha leva o foco ao Termo da anterior', async () => {
    assert.equal(await termos.nth(1).evaluate(el => el === document.activeElement), true)
  })

  // Item 2: digitando letra a letra, o texto do alerta não muda entre as teclas.
  await painel.getByRole('button', { name: 'Acrescentar termo' }).click()
  await significados.nth(2).fill('a')
  await termos.nth(2).pressSequentially('x'.repeat(41))
  const alerta = painel.getByRole('alert').filter({ hasText: 'passa de 40' })
  await alerta.waitFor()
  const antes = await alerta.innerText()
  await termos.nth(2).pressSequentially('yy')
  await conferir('item 2: texto do alerta estável entre teclas', async () => { assert.equal(await alerta.innerText(), antes) })
  await painel.getByRole('button', { name: /^Tirar o termo x/ }).click()
  assert.equal(await termos.count(), 2)

  // (c) Repetido só por maiúscula e acento: erro e salvar desabilitado.
  await painel.getByRole('button', { name: 'Acrescentar termo' }).click()
  await termos.nth(2).fill('acai')
  await significados.nth(2).fill('outra fruta')
  await painel.getByRole('alert').filter({ hasText: 'repete o termo da linha 2' }).waitFor()
  assert.equal(await painel.getByRole('button', { name: 'Salvar dicionário' }).isDisabled(), true, 'repetido trava o salvar')
  await pagina.screenshot({ path: resolve(pasta, 'repetido-1280.png'), fullPage: true })
  await painel.getByRole('button', { name: 'Tirar o termo acai' }).click()
  await painel.getByText('2 de 60 termos', { exact: true }).waitFor()
  assert.equal(await painel.getByRole('button', { name: 'Salvar dicionário' }).isDisabled(), false)

  // (d) Salvar manda só `dicionario`, aparado; a aba relê e mostra o salvo sem pendência.
  let marca = pedidos.length
  let liberarPut
  seguraPut = new Promise((r) => { liberarPut = r })
  await painel.getByRole('button', { name: 'Salvar dicionário' }).click()
  await conferir('item 1: campos e botões desabilitados enquanto salva', async () => {
    await painel.getByText('Salvando…').waitFor()
    assert.equal(await termos.first().isDisabled(), true)
    assert.equal(await significados.first().isDisabled(), true)
    assert.equal(await painel.getByRole('button', { name: 'Salvar dicionário' }).isDisabled(), true)
    assert.equal(await painel.getByRole('button', { name: 'Acrescentar termo' }).isDisabled(), true)
    assert.equal(await painel.getByRole('button', { name: 'Desfazer' }).isDisabled(), true)
    assert.equal(await painel.getByRole('button', { name: /^Tirar o termo/ }).first().isDisabled(), true)
  })
  seguraPut = null; liberarPut()
  await painel.getByText('Salvo. O próximo turno do Assistente já usa.', { exact: true }).waitFor()
  assert.deepEqual(putsDesde(marca), [{ dicionario: [{ termo: 'PF', significado: 'prato feito do dia' }, { termo: 'Açaí', significado: 'fruta roxa batida' }] }])
  assert.deepEqual(Object.keys(putsDesde(marca)[0]), ['dicionario'], 'a única chave do corpo')
  await painel.getByText('Dicionário com mudanças não salvas.').waitFor({ state: 'detached' })
  await conferir('item 1: nenhum GET novo depois de salvar', async () => { await pagina.waitForTimeout(300); assert.equal(leiturasDesde(marca), 0) })
  await conferir('item 3: foco fora do body depois de salvar', async () => { assert.equal(await focoNoBody(), false) })
  assert.deepEqual(await termos.evaluateAll(l => l.map(i => i.value)), ['PF', 'Açaí'])
  assert.deepEqual(await significados.evaluateAll(l => l.map(i => i.value)), ['prato feito do dia', 'fruta roxa batida'])
  assert.equal(await painel.getByRole('button', { name: 'Desfazer' }).count(), 0)
  assert.equal(await semRolagemLateral(), true, 'sem rolagem lateral em 1280 px')
  await pagina.screenshot({ path: resolve(pasta, 'salvo-1280.png'), fullPage: true })

  // (e) A API recusa com 400: a mensagem aparece na aba e o rascunho fica.
  marca = pedidos.length
  falhaNoProximoPut = 'O termo "PF" já existe no dicionário.'
  await significados.nth(0).fill('prato feito')
  await painel.getByRole('button', { name: 'Salvar dicionário' }).click()
  await painel.getByText('Não salvou: O termo "PF" já existe no dicionário.', { exact: true }).waitFor()
  await painel.getByText('Dicionário com mudanças não salvas.').waitFor()
  assert.equal(await significados.nth(0).inputValue(), 'prato feito', 'o rascunho fica')
  await painel.getByRole('button', { name: 'Desfazer' }).click()
  assert.equal(await significados.nth(0).inputValue(), 'prato feito do dia', 'Desfazer volta ao salvo')
  await conferir('item 3: foco fora do body depois de Desfazer', async () => { assert.equal(await focoNoBody(), false) })

  // (g) Celular: as duas colunas viram uma, sem rolagem lateral.
  await pagina.setViewportSize({ width: 375, height: 812 })
  await painel.getByRole('button', { name: 'Acrescentar termo' }).click()
  await termos.nth(2).fill('Um termo bem comprido para o celular')
  await significados.nth(2).fill('Um significado também comprido, que ocupa a largura toda da tela pequena e quebra linha')
  assert.equal(await semRolagemLateral(), true, 'sem rolagem lateral em 375 px')
  const caixa0 = await termos.nth(0).boundingBox(), caixa1 = await significados.nth(0).boundingBox()
  assert.ok(caixa1.y > caixa0.y, 'em 375 px o significado fica embaixo do termo')
  await pagina.screenshot({ path: resolve(pasta, 'dicionario-375.png'), fullPage: true })
  await pagina.setViewportSize({ width: 1280, height: 900 })

  // Item 5: tirar todas as linhas e salvar apaga; o foco vai para Acrescentar quando não sobra linha.
  await painel.getByRole('button', { name: 'Tirar o termo Um termo bem comprido para o celular' }).click()
  await painel.getByRole('button', { name: 'Tirar o termo PF' }).click()
  await conferir('item 3: tirar a primeira leva o foco ao Termo da seguinte', async () => { assert.equal(await termos.first().evaluate(el => el === document.activeElement), true) })
  await painel.getByRole('button', { name: /^Tirar o termo/ }).click()
  await conferir('item 3: sem linhas o foco vai para Acrescentar termo', async () => {
    assert.equal(await painel.getByRole('button', { name: 'Acrescentar termo' }).evaluate(el => el === document.activeElement), true)
  })
  marca = pedidos.length
  await painel.getByRole('button', { name: 'Salvar dicionário' }).click()
  await painel.getByText('Salvo. O próximo turno do Assistente já usa.', { exact: true }).waitFor()
  await conferir('item 5: apagar tudo manda lista vazia e volta ao vazio', async () => {
    assert.deepEqual(putsDesde(marca), [{ dicionario: [] }])
    await painel.getByText('Nenhum termo ainda. Acrescente o primeiro.').waitFor()
    assert.equal(await painel.getByText('Dicionário com mudanças não salvas.').count(), 0)
    assert.equal(leiturasDesde(marca), 0)
  })

  // (f) Quem não administra vê só a nota e não faz nenhum GET da configuração.
  const leitor = await contexto.newPage()
  leitor.on('pageerror', e => erros.push(e.message))
  marca = pedidos.length
  await leitor.goto(`${endereco}?editar=0`)
  const painelLeitor = leitor.getByRole('region', { name: 'Painel do Assistente' })
  await painelLeitor.getByRole('tab', { name: 'Dicionário' }).click()
  await painelLeitor.getByText('Só quem administra a loja vê e muda o dicionário.', { exact: true }).waitFor()
  await leitor.waitForTimeout(500)
  assert.equal(leiturasDesde(marca), 0, 'zero GET em /api/atendimento/configuracao')
  assert.equal(await painelLeitor.getByRole('button', { name: 'Acrescentar termo' }).count(), 0)
  await leitor.screenshot({ path: resolve(pasta, 'sem-permissao-1280.png'), fullPage: true })

  assert.deepEqual(falhas, [], falhas.join('\n'))
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, pedidos }, null, 2))
  console.log('#3149 navegador: vazio, acrescentar/editar/tirar, repetido trava, salva só `dicionario` e relê, 400 visível, leitor sem GET, 1280 e 375 px sem rolagem lateral.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await contexto.close(); await browser.close() }
