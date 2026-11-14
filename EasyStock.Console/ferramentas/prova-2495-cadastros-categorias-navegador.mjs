// #2495 (S4, I3): Chromium, componentes reais e HTTP controlado. A tela Cadastros › Categorias de produto
// cria, renomeia, move e exclui com o corpo certo, mostra a mensagem do servidor e bloqueia excluir raiz com filhas.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2495/cadastros-categorias')
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
function Cena(){const [rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();return <ContextoAcessoModulos.Provider value={{acoes:{verCadastrosProdutos:true,editarCadastrosProdutos:true},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,conversas:[],selecionada:null}}><ContextoAcoes.Provider value={{}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}><PainelCadastros tela="categorias"/></ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 1400 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))

// Banco plano; o GET monta raízes com filhas de um nível, como a API.
const banco = new Map([
  ['R1', { id: 'R1', nome: 'Bolos', categoriaPaiId: null, descricao: null }],
  ['S1', { id: 'S1', nome: 'Bolos de festa', categoriaPaiId: 'R1', descricao: null }],
  ['S2', { id: 'S2', nome: 'Bolos de pote', categoriaPaiId: 'R1', descricao: 'Pote 300 ml' }],
  ['R2', { id: 'R2', nome: 'Salgados', categoriaPaiId: null, descricao: null }],
  ['R3', { id: 'R3', nome: 'Doces', categoriaPaiId: null, descricao: null }],
])
const comProduto = new Set(['R2']), subEmUso = new Set(['S2'])
const arvore = () => [...banco.values()].filter(c => !c.categoriaPaiId).map(r => ({ ...r, empresaId: 'empresa', subCategorias: [...banco.values()].filter(s => s.categoriaPaiId === r.id).map(s => ({ ...s, empresaId: 'empresa', subCategorias: [] })) }))
const escritas = [], erros = []
let novo = 0
await contexto.route('**/api/**', async route => {
  const req = route.request(), url = new URL(req.url()), path = url.pathname
  if (!path.startsWith('/api/')) return route.continue()
  const [, , recurso, id] = path.split('/')
  assert.equal(recurso, 'categorias', path)
  if (req.method() === 'GET') return route.fulfill({ json: { data: arvore() } })
  const corpo = req.method() === 'DELETE' ? null : req.postDataJSON()
  escritas.push({ metodo: req.method(), path, empresaId: url.searchParams.get('empresaId'), corpo })
  if (req.method() === 'POST') {
    if ([...banco.values()].some(c => c.nome.toLowerCase() === corpo.nome.toLowerCase())) return route.fulfill({ status: 400, json: { error: { code: 'VALIDATION', message: `Já existe uma categoria chamada "${corpo.nome}".` } } })
    const nova = { id: `N${++novo}`, nome: corpo.nome, categoriaPaiId: corpo.categoriaPaiId, descricao: corpo.descricao }
    banco.set(nova.id, nova)
    return route.fulfill({ status: 201, json: { data: { ...nova, subCategorias: [] } } })
  }
  if (req.method() === 'PUT') {
    assert.equal(corpo.id, id, 'O PUT exige o id do corpo igual ao da rota')
    banco.set(id, { ...banco.get(id), nome: corpo.nome, descricao: corpo.descricao, categoriaPaiId: corpo.categoriaPaiId })
    return route.fulfill({ json: { data: banco.get(id) } })
  }
  if (comProduto.has(id)) return route.fulfill({ status: 400, json: { error: { code: 'VALIDATION', message: 'Nao e possivel remover uma categoria que possui produtos vinculados.' } } })
  if (subEmUso.has(id)) return route.fulfill({ status: 500, json: { error: { code: 'INTERNAL_ERROR', message: 'Erro interno' } } })
  banco.delete(id)
  return route.fulfill({ status: 204, body: '' })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const endereco = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2495/cadastros-categorias/index.html`
const botao = (nome) => pagina.getByRole('button', { name: nome, exact: true })
const dialogo = (nome) => pagina.getByRole('dialog', { name: nome, exact: true })
const noMenu = async (nome, item) => { await botao(`Mais opções de ${nome}`).click(); await pagina.getByRole('menuitem', { name: item, exact: true }).click() }
const aviso = (texto) => pagina.getByRole('status').filter({ hasText: texto }).waitFor()
try {
  await pagina.goto(endereco)
  await pagina.getByRole('heading', { name: 'Bolos', exact: true }).waitFor()
  await pagina.getByText('Bolos de pote', { exact: true }).waitFor()

  // Raiz com filhas: excluir parado e explicado.
  await pagina.screenshot({ path: resolve(pasta, 'categorias-1280.png'), fullPage: true })
  await botao('Mais opções de Bolos').click()
  assert.equal(await pagina.getByRole('menuitem', { name: 'Excluir', exact: true }).isDisabled(), true)
  await pagina.getByText('Mova ou exclua as subcategorias antes.').waitFor()
  await pagina.screenshot({ path: resolve(pasta, 'menu-bloqueado-1280.png'), fullPage: true })
  await pagina.keyboard.press('Escape')

  // Nome vazio vira erro no próprio campo, sem chamar a API.
  await botao('Nova categoria').click()
  let d = dialogo('Nova categoria'); await d.waitFor()
  await d.getByRole('button', { name: 'Criar categoria', exact: true }).click()
  await d.getByText('Informe o nome.').waitFor()
  assert.equal(await d.getByRole('textbox', { name: 'Nome' }).getAttribute('aria-invalid'), 'true')
  assert.equal(escritas.length, 0)
  await d.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await d.waitFor({ state: 'detached' })

  // Criar raiz.
  await botao('Nova categoria').click()
  d = dialogo('Nova categoria'); await d.waitFor()
  await d.getByRole('textbox', { name: 'Nome' }).fill('Tortas')
  await d.getByRole('button', { name: 'Criar categoria', exact: true }).click()
  await d.waitFor({ state: 'detached' })
  await aviso('Categoria Tortas criada.')
  assert.deepEqual(escritas.at(-1), { metodo: 'POST', path: '/api/categorias', empresaId: null, corpo: { empresaId: 'empresa', nome: 'Tortas', descricao: null, categoriaPaiId: null } })
  await pagina.getByRole('heading', { name: 'Tortas', exact: true }).waitFor()

  // Nome repetido: o diálogo fica aberto com a mensagem do servidor.
  await botao('Nova categoria').click()
  d = dialogo('Nova categoria'); await d.waitFor()
  await d.getByRole('textbox', { name: 'Nome' }).fill('Doces')
  await d.getByRole('button', { name: 'Criar categoria', exact: true }).click()
  await d.getByRole('alert').filter({ hasText: 'Já existe uma categoria chamada "Doces".' }).waitFor()
  await d.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await d.waitFor({ state: 'detached' })

  // Criar subcategoria dentro de uma raiz.
  await botao('Nova subcategoria em Bolos').click()
  d = dialogo('Nova subcategoria em Bolos'); await d.waitFor()
  await d.getByRole('textbox', { name: 'Nome' }).fill('Mini')
  await d.getByRole('button', { name: 'Criar categoria', exact: true }).click()
  await d.waitFor({ state: 'detached' })
  await aviso('Subcategoria Mini criada em Bolos.')
  assert.deepEqual(escritas.at(-1).corpo, { empresaId: 'empresa', nome: 'Mini', descricao: null, categoriaPaiId: 'R1' })

  // Renomear leva descrição e pai atuais.
  await noMenu('Bolos de pote', 'Renomear')
  d = dialogo('Renomear Bolos de pote'); await d.waitFor()
  assert.equal(await d.getByRole('textbox', { name: 'Nome' }).inputValue(), 'Bolos de pote')
  await d.getByRole('textbox', { name: 'Nome' }).fill('Potes')
  await d.getByRole('button', { name: 'Salvar nome', exact: true }).click()
  await d.waitFor({ state: 'detached' })
  await aviso('Bolos de pote agora se chama Potes.')
  assert.deepEqual(escritas.at(-1), { metodo: 'PUT', path: '/api/categorias/S2', empresaId: null, corpo: { id: 'S2', empresaId: 'empresa', nome: 'Potes', descricao: 'Pote 300 ml', categoriaPaiId: 'R1' } })

  // Mover: só raízes como destino.
  await noMenu('Bolos de festa', 'Mover para outra categoria')
  d = dialogo('Mover Bolos de festa'); await d.waitFor()
  await d.getByRole('combobox', { name: 'Mover para', exact: true }).click()
  const destinos = await d.getByRole('option').allTextContents()
  assert.deepEqual(destinos.sort(), ['Doces', 'Salgados', 'Tortas'])
  await d.getByRole('option', { name: 'Doces', exact: true }).click()
  await d.getByRole('button', { name: 'Mover', exact: true }).click()
  await d.waitFor({ state: 'detached' })
  await aviso('Subcategoria Bolos de festa movida para Doces.')
  assert.equal(escritas.at(-1).path, '/api/categorias/S1')
  assert.equal(escritas.at(-1).corpo.categoriaPaiId, 'R3')

  // Categoria com produto: a mensagem do servidor aparece e nada some.
  await noMenu('Salgados', 'Excluir')
  d = dialogo('Excluir Salgados?'); await d.waitFor()
  await d.getByRole('button', { name: 'Excluir', exact: true }).click()
  await d.getByRole('alert').filter({ hasText: 'Nao e possivel remover uma categoria que possui produtos vinculados.' }).waitFor()
  assert.equal(escritas.at(-1).metodo, 'DELETE'); assert.equal(escritas.at(-1).empresaId, 'empresa')
  await d.getByRole('button', { name: 'Cancelar', exact: true }).click()

  // Subcategoria em uso: o banco recusa (500) e a tela diz o que fazer.
  await noMenu('Potes', 'Excluir')
  d = dialogo('Excluir Potes?'); await d.waitFor()
  await d.getByRole('button', { name: 'Excluir', exact: true }).click()
  await d.getByRole('alert').filter({ hasText: 'Se algum produto usa esta subcategoria, troque a subcategoria dele antes.' }).waitFor()
  await d.getByRole('button', { name: 'Cancelar', exact: true }).click()

  // Subcategoria livre sai da lista.
  await noMenu('Mini', 'Excluir')
  d = dialogo('Excluir Mini?'); await d.waitFor()
  await d.getByRole('button', { name: 'Excluir', exact: true }).click()
  await d.waitFor({ state: 'detached' })
  await aviso('Subcategoria Mini excluída.')
  assert.equal(escritas.at(-1).path, '/api/categorias/N2')
  await pagina.getByText('Mini', { exact: true }).waitFor({ state: 'detached' })
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, escritas }, null, 2))
  console.log('#2495 S4 navegador: criar, renomear, mover e excluir com corpo certo; mensagens do servidor; raiz com filhas bloqueada.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await browser.close() }
