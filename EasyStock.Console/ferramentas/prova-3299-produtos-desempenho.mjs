// #3299: Produtos com desempenho. Chromium, componentes reais e HTTP controlado (não usa API real).
// Confere a faixa, as colunas novas, os sinais e o recolhimento no celular; tira as capturas de 1920, 1440, 390 e escuro.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const raiz = resolve(import.meta.dirname, '..'), url = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
const { chromium } = createRequire(resolve(raiz, 'package.json'))(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(raiz, '.build/3299/depois'); mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React,{useState} from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes} from '../../../src/aplicacao/contextos';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {ContextoRascunhosDosAjustes,useGravacoesDosAjustes} from '../../../src/aplicacao/rascunhosDosAjustes';
import {PainelCadastros} from '../../../src/features/gestao/cadastros/PainelCadastros';
import '../../../src/estilos/base.css';
document.body.style.cssText='margin:0;padding:16px;box-sizing:border-box;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
function Cena(){const [rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();return <ContextoAcessoModulos.Provider value={{acoes:{verCadastrosProdutos:true,editarCadastrosProdutos:true},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,conversas:[],selecionada:null}}><ContextoAcoes.Provider value={{}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}><PainelCadastros tela="produtos"/></ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
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
const resultado = {}
const browser = await chromium.launch({ headless: true })
for (const [w, h, esquema] of [[1920, 1080, 'light'], [1440, 900, 'light'], [390, 844, 'light'], [1440, 900, 'dark']]) {
  const ctx = await browser.newContext({ viewport: { width: w, height: h }, locale: 'pt-BR', colorScheme: esquema, reducedMotion: 'reduce' })
  await ctx.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture', empresa: { id: 'empresa' }, usuario: { id: 'op' }, expiraEm: Date.now() + 600000 })))
  await ctx.route('**/api/**', r => { const p = new URL(r.request().url()).pathname; if (!p.startsWith("/api/")) return r.continue()
    if (p === '/api/produtos/desempenho') return r.fulfill({ json: { data: desempenho } })
    if (p.startsWith('/api/produtos')) return r.fulfill({ json: { data: produtos, meta: { totalCount: 218 } } })
    return r.fulfill({ json: { data: [] } }) })
  const pg = await ctx.newPage(); const erros = []; pg.on('pageerror', e => erros.push(e.message))
  await pg.goto(`${url}/.build/3299/depois/index.html`)
  const linha = (n) => pg.getByRole('row').filter({ hasText: n })
  await linha('Linguini 700g').waitFor()
  const faixa = pg.getByRole('region', { name: 'Números dos produtos' })
  if (w > 700) {
    await faixa.locator('strong', { hasText: /^R\$\s1\.478$/ }).waitFor()
    assert.ok(await faixa.getByText('+32% sobre os 90 dias antes').isVisible(), 'variação da receita')
    assert.ok(await faixa.getByText('Curva A do ano (prévia)').isVisible(), 'curva prévia com base pequena')
    assert.equal(await linha('Linguini 700g').getByText('Parado', { exact: true }).count(), 1, 'Linguini parado')
    assert.equal(await linha('Lasanha Bolonhesa').getByText('Abaixo da tabela', { exact: true }).count(), 1, 'abaixo da tabela no preço')
    assert.equal(await linha('Lasanha Bolonhesa').getByText('margem 61,5%', { exact: true }).count(), 1)
    assert.equal(await linha('Molho Pomodoro').getByText('margem -20%', { exact: true }).count(), 1, 'margem negativa aparece')
    assert.equal(await linha('Fonduta').getByText('Nunca vendido', { exact: true }).count(), 1)
    assert.equal(await linha('Nhoque').getByText('Inativo', { exact: true }).count(), 1, 'inativo vira selo no nome')
    assert.equal(await pg.getByRole('columnheader', { name: 'Situação' }).count(), 0, 'sem coluna Situação')
    assert.ok(await linha('Lasanha Bolonhesa').getByText('previsão ~4 vendidos em 30 dias').isVisible(), 'previsão com unidade')
    assert.equal(await faixa.locator('details', { hasText: 'Como contamos' }).getAttribute('open'), null, 'definições recolhidas')
  } else {
    assert.equal(await faixa.locator('details').first().getAttribute('open'), null, 'no celular a faixa começa recolhida')
    assert.ok(await linha('Linguini 700g').getByText('sem venda em 90 dias · curva A · Parado').isVisible(), 'resumo sob o nome')
    assert.ok(await linha('Lasanha Bolonhesa').getByText('R$ 540 em 90 dias · curva A · Abaixo da tabela').isVisible(), 'sinal de preço no celular')
    resultado.semRolagemLateral = await pg.evaluate(() => document.documentElement.scrollWidth <= innerWidth)
    assert.ok(resultado.semRolagemLateral, 'sem rolagem lateral em 390')
  }
  assert.deepEqual(erros, [])
  await pg.screenshot({ path: resolve(pasta, `depois-${w}-${esquema}.png`), fullPage: true }); await ctx.close()
}
writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ ok: true, ...resultado }, null, 2))
await browser.close(); console.log('prova 3299 ok:', pasta)
