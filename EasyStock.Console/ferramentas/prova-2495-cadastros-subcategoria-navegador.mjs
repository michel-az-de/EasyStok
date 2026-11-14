// #2495 (S3, I2): Chromium, componentes reais e HTTP controlado. O editor de produto mostra a subcategoria
// gravada, oferece só as filhas da categoria escolhida, some sem filhas e envia `subcategoriaId` ao salvar.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2495/cadastros-subcategoria')
mkdirSync(pasta, { recursive: true })
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
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 1600 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
const categorias = [
  { id: 'R1', nome: 'Bolos', categoriaPaiId: null, subCategorias: [{ id: 'S1', nome: 'Bolos de pote', categoriaPaiId: 'R1', subCategorias: [] }, { id: 'S2', nome: 'Bolos de festa', categoriaPaiId: 'R1', subCategorias: [] }] },
  { id: 'R2', nome: 'Salgados', categoriaPaiId: null, subCategorias: [] },
]
const base = { empresaId: 'empresa', tipo: 'Alimento', status: 'Ativo', controlaValidade: false, precoReferencia: 10, revisao: 1 }
const produtos = new Map([
  ['P', { ...base, produtoId: 'P', nome: 'Pote de chocolate', categoriaId: 'R1', subcategoriaId: 'S1' }],
  ['Q', { ...base, produtoId: 'Q', nome: 'Bolo simples', categoriaId: 'R1', subcategoriaId: null }],
])
const corpos = [], erros = []
await contexto.route('**/api/**', async route => {
  const req = route.request(), url = new URL(req.url()), path = url.pathname
  if (!path.startsWith('/api/')) return route.continue()
  if (path === '/api/categorias') return route.fulfill({ json: { data: categorias } })
  const [, , recurso, id] = path.split('/')
  assert.equal(recurso, 'produtos', path)
  if (req.method() === 'GET' && !id) return route.fulfill({ json: { data: [...produtos.values()].map(p => ({ ...p, id: p.produtoId })), meta: { total: produtos.size } } })
  if (req.method() === 'GET') return route.fulfill({ json: { data: produtos.get(id) } })
  const corpo = req.postDataJSON(); corpos.push({ metodo: req.method(), path, corpo })
  produtos.set(id, { ...produtos.get(id), subcategoriaId: corpo.subcategoriaId, revisao: 2 })
  return route.fulfill({ json: { data: produtos.get(id) } })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const endereco = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2495/cadastros-subcategoria/index.html`
const escolher = async (dialogo, rotulo, opcao) => {
  await dialogo.getByRole('combobox', { name: rotulo, exact: true }).click()
  await dialogo.getByRole('option', { name: opcao, exact: true }).click()
}
try {
  await pagina.goto(endereco)
  await pagina.getByRole('row').filter({ hasText: 'Pote de chocolate' }).waitFor()

  // Produto salvo com sub abre com ela marcada.
  await pagina.getByRole('button', { name: 'Editar Pote de chocolate', exact: true }).click()
  let editor = pagina.getByRole('dialog', { name: 'Editar produto', exact: true }); await editor.waitFor()
  const sub = editor.getByRole('combobox', { name: 'Subcategoria', exact: true })
  await pagina.waitForFunction(() => document.querySelector('[role=combobox]') && [...document.querySelectorAll('label')].some(l => l.textContent === 'Subcategoria'))
  assert.equal(await sub.inputValue(), 'Bolos de pote')
  await pagina.screenshot({ path: resolve(pasta, 'produto-com-sub-1280.png'), fullPage: true })

  // Categoria sem filhas: o seletor some.
  await escolher(editor, 'Categoria', 'Salgados')
  await sub.waitFor({ state: 'detached' })
  await editor.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await editor.getByRole('button', { name: 'Descartar', exact: true }).click()
  await editor.waitFor({ state: 'detached' })

  // Escolher a sub e salvar envia `subcategoriaId`.
  await pagina.getByRole('button', { name: 'Editar Bolo simples', exact: true }).click()
  editor = pagina.getByRole('dialog', { name: 'Editar produto', exact: true }); await editor.waitFor()
  const subQ = editor.getByRole('combobox', { name: 'Subcategoria', exact: true }); await subQ.waitFor()
  assert.equal(await subQ.inputValue(), 'Sem subcategoria')
  await escolher(editor, 'Subcategoria', 'Bolos de festa')
  await editor.getByRole('button', { name: 'Salvar cadastro', exact: true }).click()
  await editor.waitFor({ state: 'detached' })
  assert.equal(corpos.at(-1).path, '/api/produtos/Q')
  assert.equal(corpos.at(-1).corpo.categoriaId, 'R1')
  assert.equal(corpos.at(-1).corpo.subcategoriaId, 'S2')
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, corpos }, null, 2))
  console.log('#2495 S3 navegador: sub gravada aparece, some sem filhas e o salvar envia subcategoriaId.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await browser.close() }
