// ModalGestao e Cadastros reais; HTTP controlado, toque em 390px. Sem API/provedor externo.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
const pasta = resolve(import.meta.dirname, '../.build/2395/retorno-gestao')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React, {useState} from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes,ContextoCatalogo} from '../../../src/aplicacao/contextos';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {ContextoRascunhosDosAjustes,useGravacoesDosAjustes} from '../../../src/aplicacao/rascunhosDosAjustes';
import {ModalGestao} from '../../../src/features/gestao/ModalGestao';
import '../../../src/estilos/base.css';
const conversa={id:'conversa-retorno',nome:'Contato de retorno',clienteId:null,vinculoRevisao:0,mensagens:[]};
window.prova={fechamentos:0,selecionada:null,toquesCliente:0};
function Cena(){const [aberto,abrir]=useState(false),[rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();
return <ContextoAcessoModulos.Provider value={{acoes:{},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,selecionada:conversa,conversas:[conversa],sessao:{empresa:{id:'empresa'},usuario:{id:'operador'}}}}><ContextoCatalogo.Provider value={{}}><ContextoAcoes.Provider value={{selecionar:id=>{window.prova.selecionada=id},carregarClienteDaConversa:()=>{}}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}>
<main><button onClick={()=>abrir(true)}>Gestão</button><div role="tablist"><button role="tab" onClick={()=>{window.prova.toquesCliente++}}>Cliente</button></div></main>
{aberto&&<ModalGestao aoFechar={()=>{window.prova.fechamentos++;abrir(false)}}/>}
</ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoCatalogo.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, locale: 'pt-BR', reducedMotion: 'reduce' })
const page = await context.newPage()
page.setDefaultTimeout(10000)
const erros = [], gravacoes = []
page.on('pageerror', e => erros.push(e.message))
await context.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({token:'fixture-controlada',empresa:{id:'empresa'},usuario:{id:'operador'},expiraEm:Date.now()+600000})))
await context.route('**/api/**', async route => {
  if (!new URL(route.request().url()).pathname.startsWith('/api/')) return route.continue()
  if (route.request().method() === 'POST') {
    assert.equal(new URL(route.request().url()).pathname, '/api/clientes')
    gravacoes.push(route.request().postDataJSON())
    return route.fulfill({ status: 201, json: { data: { id: 'cliente-novo' } } })
  }
  return route.fulfill({ json: { data: [], meta: { total: 0, page: 1, limit: 20 } } })
})
try {
  await page.goto(`${origem}/.build/2395/retorno-gestao/index.html#/m/atendimento/balcao`)
  await page.getByRole('button', { name: 'Gestão', exact: true }).tap()
  const gestao = page.getByRole('dialog', { name: 'Gestão da loja', exact: true })
  await gestao.getByRole('button', { name: 'Leads e clientes', exact: true }).tap()
  await gestao.getByRole('button', { name: 'Cadastrar pessoa da conversa', exact: true }).tap()
  const editor = page.getByRole('dialog', { name: 'Novo cliente', exact: true })
  await editor.getByRole('textbox', { name: 'Nome', exact: true }).fill('Regressão R4 retorno mobile')
  await editor.getByRole('button', { name: 'Salvar cadastro', exact: true }).tap()
  await editor.waitFor({ state: 'detached' })
  assert.equal(gravacoes.length, 1)
  await gestao.getByRole('button', { name: 'Voltar à conversa', exact: true }).tap()
  assert.equal(await gestao.count(), 0, 'Voltar à conversa fecha o ModalGestao que contém Cadastros')
  assert.equal(new URL(page.url()).hash, '#/m/atendimento/balcao')
  await page.getByRole('tab', { name: 'Cliente', exact: true }).tap()
  assert.deepEqual(await page.evaluate(() => window.prova), { fechamentos: 1, selecionada: 'conversa-retorno', toquesCliente: 1 })
  assert.deepEqual(erros, [])
  await page.screenshot({ path: resolve(pasta, 'retorno-390.png'), fullPage: true })
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ escopo: 'ModalGestao e Cadastros reais, HTTP controlado, toque390', cadastro201: 1, modalFechado: true, controleClienteLivre: true }, null, 2))
  console.log('Gestão real → cadastro201 → Voltar à conversa fecha modal e libera toque Cliente em390px.')
} catch (erro) {
  await page.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true })
  console.error(erros.join('\n'))
  throw erro
} finally { await context.close(); await browser.close() }
