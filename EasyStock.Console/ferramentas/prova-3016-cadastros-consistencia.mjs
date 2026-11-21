// #3016: Cadastros. (a) obrigatório com "*" e opcional sem marca (varredura do fonte); (b) recarregar mantém a
// lista com aria-busy e só a primeira carga usa o esqueleto (Formas de pagamento, Modelos, Etiquetas).
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'

const raiz = resolve(import.meta.dirname, '..')
const falhas = []
for (const arquivo of ['src/features/gestao/producao/AbaInsumosApi.jsx', 'src/features/gestao/financeiro/ModaisFinanceiras.jsx']) {
  const fonte = readFileSync(resolve(raiz, arquivo), 'utf8')
  if (/\bopcional\b/i.test(fonte)) falhas.push(`${arquivo}: o opcional fica sem marca; "(opcional)" convive com o padrão do "*"`)
  if (!/\bobrigatorio\b/.test(fonte)) falhas.push(`${arquivo}: o campo obrigatório não usa a prop obrigatorio ("*")`)
}

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(raiz, '.build/3016/cadastros-consistencia')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><div id="root"></div><script type="module" src="./browser.jsx"></script></html>')
writeFileSync(resolve(pasta, 'browser.jsx'), `import React from'react';import{createRoot}from'react-dom/client';import{ContextoAcessoModulos}from'../../../src/aplicacao/acessoModulos';import{contextoCadastro}from'../../../src/aplicacao/cadastros';import{MetodosCadastro}from'../../../src/features/gestao/cadastros/MetodosCadastro';import{ModelosCadastro}from'../../../src/features/gestao/cadastros/ModelosCadastro';import'../../../src/estilos/base.css';const c=contextoCadastro();createRoot(document.getElementById('root')).render(<ContextoAcessoModulos.Provider value={{acoes:{verModelosNotificacao:true,verCadastrosEtiquetas:true}}}>{location.hash==='#metodos'?<MetodosCadastro contexto={c}/>:<ModelosCadastro contexto={c} respostas={null} impressao={null}/>}</ContextoAcessoModulos.Provider>);`)

const cargas = {
  metodos: [{ codigo: 'Pix', nome: 'Pix', ativo: true }, { codigo: 'Dinheiro', nome: 'Dinheiro', ativo: true }],
  modelos: [{ id: 'm1', canal: 'Email', versao: 1, nome: 'Pedido confirmado', assuntoTemplate: 'Seu pedido', corpoTemplate: 'Oi', aprovado: true }],
  etiquetas: [{ id: 'e1', origem: 'Sistema', nome: 'Etiqueta de validade', descricao: '50 x 30 mm', isDefault: true, layoutJson: '{}' }],
}
const rotas = { metodos: '**/api/cadastros/metodos-pagamento*', modelos: '**/api/notificacoes/templates*', etiquetas: '**/api/etiquetas/templates*' }
const atualizar = page => page.getByRole('button', { name: 'Atualizar', exact: true }).click()
const telas = [
  { tela: 'metodos', nome: 'Formas de pagamento', item: 'Pix', recarregar: atualizar },
  { tela: 'modelos', nome: 'Modelos de notificação', item: 'Pedido confirmado',
    abrir: page => page.getByRole('tab', { name: 'E-mail e SMS' }).click(),
    recarregar: async page => { await page.getByRole('combobox', { name: 'Canal' }).click(); await page.getByRole('option', { name: 'SMS' }).click() } },
  { tela: 'etiquetas', nome: 'Etiquetas', item: 'Etiqueta de validade', abrir: page => page.getByRole('tab', { name: 'Etiquetas' }).click(), recarregar: atualizar },
]

const browser = await chromium.launch({ headless: true })
const erros = [], resultado = {}
const url = process.env.CONSOLE_TEST_URL ?? 'http://127.0.0.1:5209'

// A n-ésima chamada à API só responde quando `liberar(n)` roda; assim a prova olha a tela durante a espera.
async function abrirTela({ tela, abrir }) {
  const context = await browser.newContext(), page = await context.newPage(), portoes = new Map()
  let chamadas = 0
  page.on('pageerror', e => erros.push(e.message))
  await context.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture', empresa: { id: 'empresa' }, usuario: { id: 'dona' }, expiraEm: Date.now() + 600000 })))
  await page.route(rotas[tela], async r => {
    const n = ++chamadas
    await new Promise(resolve => portoes.set(n, resolve))
    await r.fulfill({ json: { data: cargas[tela], meta: { totalCount: cargas[tela].length } } })
  })
  const liberar = async n => { while (!portoes.has(n)) await new Promise(r => setTimeout(r, 20)); portoes.get(n)() }
  await page.goto(`${url}/.build/3016/cadastros-consistencia/index.html#${tela}`)
  if (abrir) await abrir(page)
  return { page, liberar, context }
}

try {
  for (const t of telas) {
    // Recarga: com itens na tela, a lista fica e o contêiner é aria-busy; ao chegar, o aria-busy sai.
    const { page, liberar, context } = await abrirTela(t)
    await liberar(1)
    await page.getByText(t.item, { exact: true }).first().waitFor()
    await t.recarregar(page)
    await page.waitForTimeout(300)
    const itens = await page.getByText(t.item, { exact: true }).count()
    const ocupadas = await page.locator('ul[aria-busy="true"]').count()
    resultado[t.tela] = { itensDuranteRecarga: itens, listaOcupada: ocupadas }
    if (itens < 1) falhas.push(`${t.nome}: ao recarregar, os itens somem da tela`)
    else if (ocupadas < 1) falhas.push(`${t.nome}: durante a recarga a lista não tem aria-busy="true"`)
    await liberar(2)
    await context.close()
    // Primeira carga: sem itens, o esqueleto de lista (output aria-busy) ocupa o lugar.
    const primeira = await abrirTela(t)
    const esqueleto = await primeira.page.locator('output[aria-busy="true"]').count()
    resultado[t.tela].esqueletoNaPrimeiraCarga = esqueleto
    if (esqueleto < 1) falhas.push(`${t.nome}: a primeira carga não mostra o esqueleto de lista`)
    await primeira.liberar(1)
    await primeira.context.close()
  }
  assert.deepEqual(falhas, [], `\n${falhas.join('\n')}`)
  assert.deepEqual(erros, [])
  console.log('Cadastros: "*" no obrigatório e opcional sem marca em Insumos e Financeiro; Formas de pagamento, Modelos e Etiquetas mantêm a lista com aria-busy na recarga e usam esqueleto na primeira carga.')
} finally {
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ resultado, falhas, erros }, null, 2)); await browser.close()
}
