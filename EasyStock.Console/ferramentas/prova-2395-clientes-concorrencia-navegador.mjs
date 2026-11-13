// Opt-in: duas abas Chromium, componentes/adaptador reais e HTTP controlado; não usa a fixture/API real.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2395/clientes-concorrencia')
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
function Cena(){const [rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();return <ContextoAcessoModulos.Provider value={{acoes:{},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,conversas:[],selecionada:null}}><ContextoAcoes.Provider value={{}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}><PainelCadastros tela="clientes"/></ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
const pessoas = new Map(['A', 'B'].map((id, i) => [id, { id, revisao: 40 + i, nome: `Pessoa ${id}`, telefone: '11911112222', email: '', tipoPessoa: 'fisica', ativo: true, orderCount: 0 }]))
const patches = [], erros = []
await contexto.route('**/api/**', async route => {
  const req = route.request(), path = new URL(req.url()).pathname
  if (!path.startsWith('/api/')) return route.continue()
  assert.ok(path.startsWith('/api/clientes'), path)
  const id = path.split('/')[3]
  if (req.method() === 'GET') return route.fulfill({ json: { data: id ? { cliente: pessoas.get(id), alteracoes: [] } : [...pessoas.values()], meta: { total: pessoas.size } } })
  assert.equal(req.method(), 'PATCH')
  const dados = req.postDataJSON(), anterior = pessoas.get(id)
  patches.push({ id, dados })
  if (dados.revisao !== anterior.revisao) return route.fulfill({ status: 409, json: { error: { code: 'CLIENTE_ALTERADO', message: 'Este cadastro mudou.' } } })
  const atual = { ...anterior, ...dados, revisao: anterior.revisao + 1 }; pessoas.set(id, atual)
  return route.fulfill({ json: { data: atual } })
})
const a = await contexto.newPage(), b = await contexto.newPage()
for (const pagina of [a, b]) { pagina.setDefaultTimeout(10000); pagina.on('pageerror', e => erros.push(e.message)) }
const url = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2395/clientes-concorrencia/index.html`
async function abrir(pagina, nome) {
  await pagina.getByRole('button', { name: `Editar ${nome}`, exact: true }).tap()
  const modal = pagina.getByRole('dialog', { name: 'Editar cliente', exact: true }); await modal.waitFor(); return modal
}
async function salvar(modal, conflito = false) {
  await modal.getByRole('button', { name: 'Salvar cadastro', exact: true }).tap()
  if (conflito) await modal.getByRole('alert').filter({ hasText: 'Este cadastro mudou.' }).waitFor()
  else await modal.waitFor({ state: 'detached' })
}
try {
  await Promise.all([a.goto(url), b.goto(url)])
  let ma = await abrir(a, 'Pessoa A'), mb = await abrir(b, 'Pessoa B')
  await ma.getByRole('textbox', { name: 'E-mail', exact: true }).fill('a@fixture.test')
  await mb.getByRole('textbox', { name: 'Telefone', exact: true }).fill('11999998888')
  await salvar(ma); await salvar(mb)
  assert.equal(pessoas.get('A').email, 'a@fixture.test'); assert.equal(pessoas.get('B').telefone, '+5511999998888') // #2457: a tela digita com máscara, a API recebe E.164
  const pessoaB = structuredClone(pessoas.get('B'))
  ma = await abrir(a, 'Pessoa A'); mb = await abrir(b, 'Pessoa A')
  await ma.getByRole('textbox', { name: 'Telefone', exact: true }).fill('11977776666')
  await mb.getByRole('textbox', { name: 'Nome', exact: true }).fill('Pessoa A digitada')
  await salvar(ma); await salvar(mb, true)
  assert.equal(await mb.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), 'Pessoa A digitada')
  assert.equal(await mb.getByRole('button', { name: 'Salvar cadastro', exact: true }).isDisabled(), true)
  let escritas = patches.length
  await mb.getByRole('button', { name: 'Conferir versão atual', exact: true }).tap()
  await mb.getByText('Atual: (11) 97777-6666', { exact: false }).waitFor()
  assert.equal(patches.length, escritas, 'Conferir é somente leitura')
  await b.screenshot({ path: resolve(pasta, 'conflito-reconferencia-390.png'), fullPage: true })
  // Outra gravação entre a leitura de conferência e a nova confirmação exige conferir de novo.
  ma = await abrir(a, 'Pessoa A')
  await ma.getByRole('textbox', { name: 'E-mail', exact: true }).fill('posterior@fixture.test')
  await salvar(ma)
  escritas = patches.length
  await mb.getByRole('button', { name: 'Manter minhas alterações sobre esta versão', exact: true }).tap()
  assert.equal(patches.length, escritas, 'Adotar a versão não grava')
  assert.equal(await mb.getByRole('textbox', { name: 'Telefone', exact: true }).inputValue(), '(11) 97777-6666')
  await salvar(mb, true)
  assert.equal(pessoas.get('A').nome, 'Pessoa A')
  await mb.getByRole('button', { name: 'Conferir versão atual', exact: true }).tap()
  await mb.getByRole('button', { name: 'Manter minhas alterações sobre esta versão', exact: true }).tap()
  assert.equal(await mb.getByRole('textbox', { name: 'E-mail', exact: true }).inputValue(), 'posterior@fixture.test')
  await salvar(mb)
  assert.equal(pessoas.get('A').nome, 'Pessoa A digitada')
  assert.equal(pessoas.get('A').telefone, '+5511977776666')
  assert.equal(pessoas.get('A').email, 'posterior@fixture.test')
  assert.deepEqual(pessoas.get('B'), pessoaB)
  assert.deepEqual(erros, [])
  assert.ok(await b.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
  await b.screenshot({ path: resolve(pasta, 'reconfirmado-390.png'), fullPage: true })
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, duasPessoasEmDuasAbas: true, mesmaPessoa409: true, draftPreservado: true, reconferenciaSemEscrita: true, novoConflitoAposLeitura: true, camposNaoEditadosPreservados: true, patches }, null, 2))
  console.log('C01 clientes: duas pessoas preservadas; mesma pessoa409; rascunho e campos alheios preservados; reconferência sem gravar e nova confirmação explícita.')
} catch (erro) { await b.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await contexto.close(); await browser.close() }
