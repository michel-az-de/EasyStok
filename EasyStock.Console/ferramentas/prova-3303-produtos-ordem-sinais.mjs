// #3303: Produtos ordenados e filtrados pelas vendas. Base da prova da #3299. Chromium, componentes reais e HTTP controlado (não usa API real).
// Confere as 2 ordens novas, os contadores da faixa como filtro, a Situação travada, a busca que solta o filtro e 1440, 390 e escuro.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const raiz = resolve(import.meta.dirname, '..'), url = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
const { chromium } = createRequire(resolve(raiz, 'package.json'))(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(raiz, '.build/3303/depois'); mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React,{useState} from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes} from '../../../src/aplicacao/contextos';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {ContextoRascunhosDosAjustes,useGravacoesDosAjustes} from '../../../src/aplicacao/rascunhosDosAjustes';
import {PainelCadastros} from '../../../src/features/gestao/cadastros/PainelCadastros';
import '../../../src/estilos/base.css';
document.body.style.cssText='margin:0;padding:16px;box-sizing:border-box;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
function Cena(){const [rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();return <ContextoAcessoModulos.Provider value={{acoes:{verCadastrosProdutos:true,editarCadastrosProdutos:!location.search.includes('visualizador')},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,conversas:[],selecionada:null}}><ContextoAcoes.Provider value={{}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}><PainelCadastros tela="produtos"/></ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
for (const e of [document.documentElement, document.body, document.getElementById('root')]) { e.style.height = 'auto'; e.style.overflow = 'visible' } // a app real rola no layout; aqui a página rola
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const nomes = [['Lasanha Bolonhesa Clássica 800g',45],['Linguini 700g',19],['Ravioli de limão siciliano e mussarela 500g',38],['Sorrentino de ricota e espinafre 700g',31],['Capeletti de frango 700g',31],['Ravioli massa de abóbora e alho poró 500g',39],['Panqueca de frango e creme de milho',28],['Molho Pomodoro 250ml',15],['Fonduta de parmesão',28],['Tortellini de costela kilo',82],['Nhoque de mandioquinha',69],['Talharim à bolonhesa',46]]
const produtos = nomes.map(([nome, preco], i) => ({ id: `p${i}`, produtoId: `p${i}`, nome, precoReferencia: preco, status: i > 9 ? 'Inativo' : 'Ativo', categoriaId: 'c1', revisao: 1 }))
const D = (i, o) => ({ produtoId: `p${i}`, receita: 0, unidades: 0, pedidos: 0, receitaAnterior: 0, fatiaDaReceita: 0, tendencia: null, ritmoDe30Dias: null, curva: null, ultimaVenda: null, diasSemVender: null, pedidosNoHistorico: 0, parado: false, nuncaVendeu: false, precoVendido: null, precoTabela: nomes[i][1], custo: null, margem: null, abaixoDaTabela: false, ...o })
const desempenho = { janelaDias: 90, hoje: '2026-11-24', resumo: { receita: 1478, receitaAnterior: 1120, compras: 21, comprasAnterior: 17, produtosVendidos: 7, receitaSemProduto: 64, ativos: 10, parados: 2, nuncaVenderam: 1, abaixoDaTabela: 1, semCusto: 8, semPreco: 0,
  curva: { produtosA: 4, fatiaA: 78.4, comprasNoAno: 74, previa: true }, maisVendidos: [{ produtoId: 'p0', nome: nomes[0][0], receita: 540 }, { produtoId: 'p2', nome: nomes[2][0], receita: 228 }, { produtoId: 'p5', nome: nomes[5][0], receita: 177 }] },
  produtos: [D(0, { receita: 540, unidades: 12, pedidos: 9, receitaAnterior: 279, fatiaDaReceita: 36.5, tendencia: 'subindo', ritmoDe30Dias: 4, curva: 'A', ultimaVenda: '2026-11-14', diasSemVender: 10, pedidosNoHistorico: 19, precoVendido: 39, custo: 15, margem: 61.5, abaixoDaTabela: true }),
    D(1, { curva: 'A', ultimaVenda: '2026-08-02', diasSemVender: 114, pedidosNoHistorico: 11, parado: true, precoVendido: 19 }),
    D(2, { receita: 228, unidades: 6, pedidos: 4, receitaAnterior: 0, fatiaDaReceita: 15.4, ritmoDe30Dias: 2, curva: 'A', ultimaVenda: '2026-10-01', diasSemVender: 54, pedidosNoHistorico: 5, precoVendido: 38 }),
    D(3, { curva: 'B', ultimaVenda: '2026-08-02', diasSemVender: 114, pedidosNoHistorico: 4, parado: true, precoVendido: 31, custo: 12, margem: 61.3 }),
    D(4, { receita: 93, unidades: 3, pedidos: 3, receitaAnterior: 155, fatiaDaReceita: 6.3, tendencia: 'caindo', ritmoDe30Dias: 1, curva: 'B', ultimaVenda: '2026-11-01', diasSemVender: 23, pedidosNoHistorico: 9, precoVendido: 31 }),
    D(5, { receita: 177, unidades: 5, pedidos: 3, receitaAnterior: 160, fatiaDaReceita: 12, tendencia: 'estavel', ritmoDe30Dias: 1.7, curva: 'A', ultimaVenda: '2026-10-24', diasSemVender: 31, pedidosNoHistorico: 6, precoVendido: 35.4 }),
    D(6, { receita: 136, unidades: 4, pedidos: 2, fatiaDaReceita: 9.2, curva: 'B', ultimaVenda: '2026-10-14', diasSemVender: 41, pedidosNoHistorico: 2, precoVendido: 34 }),
    D(7, { receita: 90, unidades: 6, pedidos: 3, fatiaDaReceita: 6.1, ritmoDe30Dias: 2, curva: 'C', ultimaVenda: '2026-11-04', diasSemVender: 20, pedidosNoHistorico: 3, precoVendido: 15, custo: 18, margem: -20 }),
    D(8, { nuncaVendeu: true }),
    D(9, { receita: 82, unidades: 1, pedidos: 1, fatiaDaReceita: 5.5, curva: 'C', ultimaVenda: '2026-11-01', diasSemVender: 23, pedidosNoHistorico: 1, precoVendido: 82 }),
    D(10, { curva: 'C', ultimaVenda: '2026-05-07', diasSemVender: 201, pedidosNoHistorico: 1, precoVendido: 69 })] }
const resultado = { pedidos: [] }
const browser = await chromium.launch({ headless: true })
for (const [w, h, esquema] of [[1440, 900, 'light'], [390, 844, 'light'], [1440, 900, 'dark']]) {
  const ctx = await browser.newContext({ viewport: { width: w, height: h }, locale: 'pt-BR', colorScheme: esquema, reducedMotion: 'reduce' })
  await ctx.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture', empresa: { id: 'empresa' }, usuario: { id: 'op' }, expiraEm: Date.now() + 600000 })))
  const pedidas = []
  await ctx.route('**/api/**', r => { const u = new URL(r.request().url()), p = u.pathname; if (!p.startsWith('/api/')) return r.continue()
    if (p === '/api/produtos/desempenho') return r.fulfill({ json: { data: desempenho } })
    if (p === '/api/produtos/desempenho/lista') {
      pedidas.push(u.search)
      const sinal = u.searchParams.get('sinal'), sort = u.searchParams.get('sort')
      const ids = sinal === 'Parado' ? [3, 1] : sort === 'receita90' ? [0, 2, 5, 6, 4, 7, 9, 1, 3, 8, 10, 11] : [10, 1, 3, 9, 4, 6, 7, 5, 2, 0, 8, 11]
      return r.fulfill({ json: { data: ids.map(i => produtos[i]), meta: { totalCount: ids.length } } })
    }
    if (p.startsWith('/api/produtos')) { pedidas.push(`padrao${u.search}`); return r.fulfill({ json: { data: produtos, meta: { totalCount: 218 } } }) }
    return r.fulfill({ json: { data: [] } }) })
  const pg = await ctx.newPage(); const erros = []; pg.on('pageerror', e => erros.push(e.message))
  await pg.goto(`${url}/.build/3303/depois/index.html`)
  const nomesNaTabela = async () => (await pg.getByRole('row').allInnerTexts()).slice(1).map(t => t.split('\n')[0].trim())
  const faixa = pg.getByRole('region', { name: 'Números dos produtos' })
  await pg.getByRole('row').filter({ hasText: 'Linguini 700g' }).waitFor()
  if (w <= 700) await faixa.locator('summary').first().click()
  // Ordem por vendas: vai à rota do desempenho com sort=receita90 e a tabela segue a ordem do servidor.
  const ordem = pg.getByRole('combobox', { name: 'Ordem' })
  await ordem.focus(); await ordem.fill('Maior receita'); await pg.getByRole('option', { name: 'Maior receita (90 dias)' }).waitFor()
  await Promise.all([pg.waitForResponse(r => r.url().includes('/desempenho/lista') && r.url().includes('sort=receita90')), pg.keyboard.press('Enter')])
  await pg.waitForFunction(() => document.querySelectorAll('tbody tr')[1]?.innerText.includes('Ravioli de limão'))
  assert.ok(pedidas.some(q => q.includes('sort=receita90') && q.includes('order=desc')), 'ordem por receita vai ao servidor')
  assert.match((await nomesNaTabela()).slice(0, 2).join('|'), /Lasanha.*\|Ravioli de limão/, 'tabela na ordem do servidor')
  // Contador da faixa filtra: "2 parados" vira botão apertado, Situação trava e a barra diz o filtro.
  const parados = faixa.getByRole('button', { name: /2\s+parados/ })
  await Promise.all([pg.waitForResponse(r => r.url().includes('sinal=Parado')), parados.click()])
  await pg.getByText('Mostrando só os produtos').waitFor()
  await pg.waitForFunction(() => document.querySelectorAll('tbody tr').length === 2)
  assert.equal(await parados.getAttribute('aria-pressed'), 'true', 'contador apertado')
  assert.ok(pedidas.some(q => q.includes('sinal=Parado')), 'filtro vai como sinal')
  assert.equal((await nomesNaTabela()).length, 2, 'só os 2 parados')
  assert.ok(await pg.getByRole('combobox', { name: 'Situação' }).isDisabled(), 'Situação travada com filtro de sinal')
  assert.equal(await faixa.getByRole('button', { name: /sem preço/ }).count(), 0, 'sem preço não vira filtro')
  // Mostrar todos solta o filtro.
  await pg.getByRole('button', { name: 'Mostrar todos' }).click()
  await pg.getByText('Mostrando só os produtos').waitFor({ state: 'detached' })
  assert.equal(await parados.getAttribute('aria-pressed'), 'false', 'contador solto')
  // Busca de texto solta o sinal (ela vai por outra rota).
  await parados.click(); await pg.getByText('Mostrando só os produtos').waitFor()
  await pg.getByRole('combobox', { name: 'Buscar' }).or(pg.getByRole('textbox', { name: 'Buscar' })).first().fill('lasanha')
  await pg.getByText('Mostrando só os produtos').waitFor({ state: 'detached' })
  resultado.semRolagemLateral = await pg.evaluate(() => document.documentElement.scrollWidth <= innerWidth)
  if (w <= 700) assert.ok(resultado.semRolagemLateral, 'sem rolagem lateral em 390')
  assert.deepEqual(erros, [])
  await pg.getByRole('combobox', { name: 'Buscar' }).or(pg.getByRole('textbox', { name: 'Buscar' })).first().fill('')
  await parados.click(); await pg.getByText('Mostrando só os produtos').waitFor()
  await pg.screenshot({ path: resolve(pasta, `depois-${w}-${esquema}.png`), fullPage: true })
  resultado.pedidos.push(...pedidas); await ctx.close()
}
// Visualizador (sem editar produtos, a porta Operador da API): sem números, sem ordens por vendas e sem chamar as rotas delas.
{ const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'pt-BR' })
  await ctx.addInitScript(() => { sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture', empresa: { id: 'empresa' }, usuario: { id: 'op' }, expiraEm: Date.now() + 600000 })) })
  const pedidas = []
  await ctx.route('**/api/**', r => { const u = new URL(r.request().url()); if (!u.pathname.startsWith('/api/')) return r.continue(); pedidas.push(u.pathname); return r.fulfill({ json: { data: u.pathname === '/api/produtos' ? produtos : [], meta: { totalCount: 12 } } }) })
  const pg = await ctx.newPage(); const erros = []; pg.on('pageerror', e => erros.push(e.message))
  await pg.goto(`${url}/.build/3303/depois/index.html?visualizador`)
  await pg.getByRole('row').filter({ hasText: 'Linguini 700g' }).waitFor()
  const ordem = pg.getByRole('combobox', { name: 'Ordem' }); await ordem.focus(); await ordem.fill('')
  await pg.keyboard.press('ArrowDown')
  await pg.getByRole('option', { name: 'Mais recentes' }).waitFor()
  assert.equal(await pg.getByRole('option', { name: 'Maior receita (90 dias)' }).count(), 0, 'visualizador sem ordem por vendas')
  assert.equal(await pg.getByRole('region', { name: 'Números dos produtos' }).count(), 0, 'visualizador sem faixa')
  assert.ok(!pedidas.some(p => p.startsWith('/api/produtos/desempenho')), 'visualizador não chama as rotas de Operador')
  assert.deepEqual(erros, []); resultado.visualizador = pedidas; await ctx.close() }
writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ ok: true, ...resultado }, null, 2))
await browser.close(); console.log('prova 3303 ok:', pasta)
