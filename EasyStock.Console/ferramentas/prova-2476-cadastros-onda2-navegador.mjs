// #2476 (onda 2 de Cadastros): Chromium, componentes reais e HTTP controlado; não usa a fixture/API real.
// Confere a pílula só na linha que pede ação, o CPF na máscara da tela e os dígitos no corpo enviado.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2476/cadastros-onda2')
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
const contexto = await browser.newContext({ viewport: { width: 1280, height: 800 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
const pessoas = new Map([
  ['A', { id: 'A', revisao: 7, nome: 'Pessoa Lead', telefone: '11911112222', email: '', documento: '', tipoPessoa: 'fisica', ativo: true, orderCount: 0 }],
  ['B', { id: 'B', revisao: 3, nome: 'Pessoa Cliente', telefone: '11933334444', email: '', documento: '98765432100', tipoPessoa: 'fisica', ativo: true, orderCount: 4 }],
])
const patches = [], erros = []
await contexto.route('**/api/**', async route => {
  const req = route.request(), url = new URL(req.url()), path = url.pathname
  if (!path.startsWith('/api/')) return route.continue()
  assert.ok(path.startsWith('/api/clientes'), path)
  const id = path.split('/')[3]
  if (req.method() === 'GET') {
    if (!id) assert.equal(url.searchParams.get('pageSize'), '20', 'A lista pede a página da constante do domínio')
    return route.fulfill({ json: { data: id ? { cliente: pessoas.get(id), alteracoes: [] } : [...pessoas.values()], meta: { total: pessoas.size } } })
  }
  assert.equal(req.method(), 'PATCH')
  const dados = req.postDataJSON()
  patches.push({ id, dados })
  const atual = { ...pessoas.get(id), ...dados, revisao: pessoas.get(id).revisao + 1 }; pessoas.set(id, atual)
  return route.fulfill({ json: { data: atual } })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const endereco = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2476/cadastros-onda2/index.html`
try {
  await pagina.goto(endereco)
  const linha = (nome) => pagina.getByRole('row').filter({ hasText: nome })
  await linha('Pessoa Lead').waitFor()
  // U6: só o Lead pede ação (pílula); Cliente vira texto na coluna Situação.
  assert.equal(await linha('Pessoa Lead').locator('[class*="pilula"]').count(), 1, 'Lead leva pílula')
  assert.equal(await linha('Pessoa Cliente').locator('[class*="pilula"]').count(), 0, 'Cliente é texto, sem pílula')
  await linha('Pessoa Cliente').getByText('Cliente', { exact: true }).waitFor()
  await pagina.screenshot({ path: resolve(pasta, 'lista-1280.png'), fullPage: true })

  // U2: documento gravado só em dígitos aparece na máscara; o digitado também.
  await pagina.getByRole('button', { name: 'Editar Pessoa Cliente', exact: true }).click()
  let modal = pagina.getByRole('dialog', { name: 'Editar cliente', exact: true }); await modal.waitFor()
  assert.equal(await modal.getByRole('textbox', { name: 'CPF ou documento', exact: true }).inputValue(), '987.654.321-00')
  assert.equal(await modal.getByRole('button', { name: 'Salvar cadastro', exact: true }).isDisabled(), true, 'Abrir não muda nada')
  await modal.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await modal.waitFor({ state: 'detached' })

  await pagina.getByRole('button', { name: 'Editar Pessoa Lead', exact: true }).click()
  modal = pagina.getByRole('dialog', { name: 'Editar cliente', exact: true }); await modal.waitFor()
  const doc = modal.getByRole('textbox', { name: 'CPF ou documento', exact: true })
  await doc.pressSequentially('12345678900')
  assert.equal(await doc.inputValue(), '123.456.789-00')
  await pagina.screenshot({ path: resolve(pasta, 'cpf-mascarado-1280.png'), fullPage: true })
  await modal.getByRole('button', { name: 'Salvar cadastro', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  assert.equal(patches.length, 1)
  assert.equal(patches[0].dados.documento, '12345678900', 'A API recebe só os dígitos')
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, pilulaSoNoLead: true, cpfMascarado: true, digitosNaApi: true, patches }, null, 2))
  console.log('#2476 navegador: pílula só no Lead; CPF na máscara da tela e só dígitos na API.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await contexto.close(); await browser.close() }
