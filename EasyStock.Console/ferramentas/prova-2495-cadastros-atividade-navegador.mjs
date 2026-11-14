// #2495 (S2, I1): Chromium, componentes reais e HTTP controlado. Inativar e reativar pelo editor, com
// confirmação; cancelar não chama a API; o 400 do fornecedor com pedido aberto fica no Modal.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2495/cadastros-atividade')
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
const tela = new URLSearchParams(location.search).get('tela') ?? 'clientes';
function Cena(){const [rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();return <ContextoAcessoModulos.Provider value={{acoes:{editarCadastrosFornecedores:true,verCadastrosFornecedores:true},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,conversas:[],selecionada:null}}><ContextoAcoes.Provider value={{}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}><PainelCadastros tela={tela}/></ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 1400 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
const registros = {
  clientes: new Map([
    ['A', { id: 'A', revisao: 1, nome: 'Ana Ativa', telefone: '', email: '', documento: '', tipoPessoa: 'fisica', ativo: true, orderCount: 2 }],
    ['B', { id: 'B', revisao: 1, nome: 'Bia Inativa', telefone: '', email: '', documento: '', tipoPessoa: 'fisica', ativo: false, orderCount: 1 }],
  ]),
  fornecedores: new Map([['F', { id: 'F', nome: 'Moinho Aberto', telefone: '', email: '', documento: '', ativo: true }]]),
}
const escritas = [], erros = []
await contexto.route('**/api/**', async route => {
  const req = route.request(), url = new URL(req.url()), path = url.pathname
  if (!path.startsWith('/api/')) return route.continue()
  const [, , recurso, id, acao] = path.split('/')
  const mapa = registros[recurso]
  assert.ok(mapa, path)
  if (req.method() === 'GET') {
    if (!id) return route.fulfill({ json: { data: [...mapa.values()], meta: { total: mapa.size } } })
    const item = mapa.get(id)
    return route.fulfill({ json: { data: recurso === 'clientes' ? { cliente: item, alteracoes: [] } : item } })
  }
  escritas.push({ metodo: req.method(), path, empresaId: url.searchParams.get('empresaId') })
  if (recurso === 'fornecedores') return route.fulfill({ status: 400, json: { error: { code: 'VALIDATION', message: 'Nao e permitido desativar fornecedor com pedido aberto ou em transito.' } } })
  if (req.method() === 'DELETE') { mapa.get(id).ativo = false; return route.fulfill({ status: 204, body: '' }) }
  assert.equal(acao, 'reativar')
  mapa.get(id).ativo = true
  return route.fulfill({ json: { data: { reativado: true } } })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2495/cadastros-atividade/index.html`
const linha = (nome) => pagina.getByRole('row').filter({ hasText: nome })
try {
  await pagina.goto(base)
  await linha('Ana Ativa').waitFor()

  // Cancelar não chama a API.
  await pagina.getByRole('button', { name: 'Editar Ana Ativa', exact: true }).click()
  let editor = pagina.getByRole('dialog', { name: 'Editar cliente', exact: true }); await editor.waitFor()
  await editor.getByRole('button', { name: 'Inativar', exact: true }).click()
  let confirmar = pagina.getByRole('dialog', { name: 'Inativar Ana Ativa?', exact: true }); await confirmar.waitFor()
  await pagina.screenshot({ path: resolve(pasta, 'confirmar-inativar-1280.png'), fullPage: true })
  await confirmar.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await confirmar.waitFor({ state: 'detached' })
  assert.equal(escritas.length, 0, 'Cancelar não envia nada')

  // Confirmar: DELETE, editor fecha, aviso e lista com Inativo.
  await editor.getByRole('button', { name: 'Inativar', exact: true }).click()
  confirmar = pagina.getByRole('dialog', { name: 'Inativar Ana Ativa?', exact: true }); await confirmar.waitFor()
  await confirmar.getByRole('button', { name: 'Inativar cliente', exact: true }).click()
  await editor.waitFor({ state: 'detached' })
  await pagina.getByRole('status').filter({ hasText: 'O cadastro de Ana Ativa foi inativado.' }).waitFor()
  assert.deepEqual(escritas.at(-1), { metodo: 'DELETE', path: '/api/clientes/A', empresaId: 'empresa' })
  await linha('Ana Ativa').getByText('Inativo', { exact: true }).waitFor()

  // Reativar a inativa: POST /reativar.
  await pagina.getByRole('button', { name: 'Editar Bia Inativa', exact: true }).click()
  editor = pagina.getByRole('dialog', { name: 'Editar cliente', exact: true }); await editor.waitFor()
  await editor.getByRole('button', { name: 'Reativar', exact: true }).click()
  confirmar = pagina.getByRole('dialog', { name: 'Reativar Bia Inativa?', exact: true }); await confirmar.waitFor()
  await confirmar.getByRole('button', { name: 'Reativar cliente', exact: true }).click()
  await editor.waitFor({ state: 'detached' })
  assert.deepEqual(escritas.at(-1), { metodo: 'POST', path: '/api/clientes/B/reativar', empresaId: 'empresa' })

  // Com alteração sem salvar, Inativar fica parado com motivo (fechar perderia o que foi digitado).
  await pagina.getByRole('button', { name: 'Editar Bia Inativa', exact: true }).click()
  editor = pagina.getByRole('dialog', { name: 'Editar cliente', exact: true }); await editor.waitFor()
  await editor.getByRole('textbox', { name: 'E-mail', exact: true }).fill('bia@exemplo.com')
  assert.equal(await editor.getByRole('button', { name: 'Inativar', exact: true }).isDisabled(), true, 'Com edição pendente não inativa')
  await editor.getByRole('textbox', { name: 'E-mail', exact: true }).fill('')

  // Fornecedor com pedido aberto: o 400 fica no Modal, em frase legível, e nada fecha.
  await pagina.goto(`${base}?tela=fornecedores`)
  await linha('Moinho Aberto').waitFor()
  await pagina.getByRole('button', { name: 'Editar Moinho Aberto', exact: true }).click()
  editor = pagina.getByRole('dialog', { name: 'Editar fornecedor', exact: true }); await editor.waitFor()
  await editor.getByRole('button', { name: 'Inativar', exact: true }).click()
  confirmar = pagina.getByRole('dialog', { name: 'Inativar Moinho Aberto?', exact: true }); await confirmar.waitFor()
  await confirmar.getByRole('button', { name: 'Inativar fornecedor', exact: true }).click()
  await confirmar.getByRole('alert').filter({ hasText: 'pedido de compra aberto ou em trânsito' }).waitFor()
  assert.equal(await confirmar.isVisible(), true, 'O Modal segue aberto com o erro')
  assert.deepEqual(escritas.at(-1), { metodo: 'DELETE', path: '/api/fornecedores/F', empresaId: 'empresa' })
  await pagina.screenshot({ path: resolve(pasta, 'fornecedor-pedido-aberto-1280.png'), fullPage: true })
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, escritas }, null, 2))
  console.log('#2495 S2 navegador: inativar e reativar com confirmação; cancelar não grava; erro do fornecedor fica no Modal.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await browser.close() }
