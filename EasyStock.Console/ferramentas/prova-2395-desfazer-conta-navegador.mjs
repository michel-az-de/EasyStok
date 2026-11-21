// Cadastros e adaptador reais em duas abas; servidor HTTP controlado com revisão por conta.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2395/desfazer-conta')
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
function Cena(){const [rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();return <ContextoAcessoModulos.Provider value={{acoes:{verCadastrosFinanceiros:true,gerenciarCadastrosFinanceiros:true},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,conversas:[],selecionada:null}}><ContextoAcoes.Provider value={{}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}><PainelCadastros tela="bancos"/></ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
const contas = new Map(['A', 'B'].map(id => [id, { id, nome: `Regressão R4 Conta ${id}`, instituicao: 'Original', tipo: 'corrente', ativo: true, revisao: 0 }]))
const patches = [], erros = []
await contexto.route('**/api/**', async route => {
  const req = route.request(), path = new URL(req.url()).pathname
  if (!path.startsWith('/api/')) return route.continue()
  assert.ok(path.startsWith('/api/cadastros/contas-bancarias'), `Requisição inesperada: ${path}`)
  if (req.method() === 'GET') return route.fulfill({ json: { data: [...contas.values()] } })
  assert.equal(req.method(), 'PATCH')
  const dados = req.postDataJSON(), id = path.split('/').at(-1), anterior = contas.get(id)
  patches.push({ id, dados })
  if (dados.revisao !== anterior.revisao) return route.fulfill({ status: 409, json: { message: 'Esta conta mudou.' } })
  const atual = { ...dados, revisao: anterior.revisao + 1 }; contas.set(id, atual)
  return route.fulfill({ json: { data: atual } })
})
const a = await contexto.newPage(), b = await contexto.newPage()
for (const pagina of [a, b]) { pagina.setDefaultTimeout(10000); pagina.on('pageerror', e => erros.push(e.message)) }
const url = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2395/desfazer-conta/index.html`
async function editar(pagina, conta, instituicao) {
  await pagina.getByRole('button', { name: `Editar Regressão R4 Conta ${conta}`, exact: true }).tap()
  const modal = pagina.getByRole('dialog', { name: 'Editar cadastro bancário', exact: true })
  await modal.getByRole('textbox', { name: 'Instituição financeira', exact: true }).fill(instituicao)
  await modal.getByRole('button', { name: 'Salvar cadastro', exact: true }).tap()
  await modal.waitFor({ state: 'detached' })
}
try {
  await Promise.all([a.goto(url), b.goto(url)])
  await editar(a, 'A', 'Edição A')
  await editar(b, 'B', 'Edição B posterior')
  await a.getByRole('button', { name: 'Desfazer edição desta conta', exact: true }).tap()
  await a.getByRole('status').filter({ hasText: 'Edição desfeita somente' }).waitFor()
  assert.equal(contas.get('A').instituicao, 'Original')
  assert.equal(contas.get('B').instituicao, 'Edição B posterior')
  assert.equal(patches[2].dados.revisao, 1)
  assert.notEqual(patches[2].dados.requestId, patches[0].dados.requestId)
  await a.screenshot({ path: resolve(pasta, 'B-preservada-390.png'), fullPage: true })
  await editar(a, 'A', 'Edição A para conflito')
  await b.reload()
  await editar(b, 'A', 'A posterior em outra aba')
  await a.getByRole('button', { name: 'Desfazer edição desta conta', exact: true }).tap()
  await a.getByRole('alert').filter({ hasText: 'A conta mudou depois desta edição' }).waitFor()
  assert.equal(contas.get('A').instituicao, 'A posterior em outra aba')
  assert.equal(contas.get('B').instituicao, 'Edição B posterior')
  assert.equal(contas.get('A').revisao, 4)
  assert.equal(patches.at(-1).dados.revisao, 3, 'Desfazer usa revisão da própria gravação, não uma revisão recém-lida')
  assert.equal(await a.getByRole('button', { name: 'Desfazer edição desta conta', exact: true }).count(), 0)
  await a.screenshot({ path: resolve(pasta, 'A-posterior-409-390.png'), fullPage: true })
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, duasAbas: true, desfazerSoUmaConta: true, escritaBPreservada: true, mesmaContaPosterior409: true, patches }, null, 2))
  console.log('Desfazer edição: PATCH só na conta A, revisão original e requestId novo; B posterior preservada; A posterior recusa409.')
} catch (erro) { await a.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await contexto.close(); await browser.close() }
