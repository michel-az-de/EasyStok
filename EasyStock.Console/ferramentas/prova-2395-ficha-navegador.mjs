// Ficha e Cadastros reais em Chromium; HTTP controlado para observar identidade e conflito.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2395/ficha-identidade')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1"><div id="root"></div><script type="module" src="./browser.jsx"></script></html>')
writeFileSync(resolve(pasta, 'browser.jsx'), `
import React,{useState,useRef,useCallback} from 'react';import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoCatalogo,ContextoAcoes} from '../../../src/aplicacao/contextos';
import {ContextoRascunhosDosAjustes,useGravacoesDosAjustes} from '../../../src/aplicacao/rascunhosDosAjustes';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {criarAcoesClienteApi} from '../../../src/aplicacao/api/cliente';
import {BlocoCliente} from '../../../src/features/ficha-cliente/BlocoCliente';
import {PainelCadastros} from '../../../src/features/gestao/cadastros/PainelCadastros';
import {CANAIS} from '../../../src/infra/catalogo';import '../../../src/estilos/base.css';
const original={id:'conversa',clienteId:'a',vinculoRevisao:4,nome:'Pessoa A',canal:'WhatsApp',estado:'Aberto',conta:'cliente',cliente:{tags:[],notas:[],pedidos:0,pontos:0,telefone:'(11) 99999-9999',endereco:'Rua A, 10, Centro, 01001-000'},pedido:null,mensagens:[{id:'msg',dir:'in',texto:'Meu nome é Júlia, telefone11999999999 e email julia@exemplo.com'}]};
function Teste(){const [conversa,trocar]=useState(original),[modo,definirModo]=useState('ficha'),[montado,montar]=useState(true),[rascunhos,guardar]=useState({});const gravacoes=useGravacoesDosAjustes();
const definir=useCallback((chave,valor)=>guardar(e=>({...e,[chave]:valor})),[]);const estado={fonteApi:true,selecionada:conversa,conversas:[conversa],historico:[],agora:Date.now(),automaticoPausado:{},fidelidade:{recompensas:[],sorteios:[],resgatesPorCadastro:{}},sessao:{empresa:{id:'empresa'},usuario:{id:'dona'}}};const ref=useRef(estado);ref.current=estado;
const api=criarAcoesClienteApi({estadoRef:ref,despachar:()=>{}});window.trocarPessoa=()=>trocar(c=>({...c,clienteId:c.clienteId==='b'?'a':'b',nome:c.clienteId==='b'?'Pessoa A':'Pessoa B',vinculoRevisao:c.vinculoRevisao+1}));window.remontarFicha=()=>{montar(false);setTimeout(()=>montar(true),50)};
window.abrirNovoLead=()=>{trocar({...original,clienteId:null,vinculoRevisao:0,conta:'lead',nome:'Contato novo',cliente:{...original.cliente,telefone:null,endereco:null}});definirModo('cadastros')};window.rascunhos=rascunhos;
return <ContextoAcessoModulos.Provider value={{acoes:{},permite:()=>true}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir,...gravacoes}}><ContextoEstado.Provider value={estado}><ContextoAcoes.Provider value={{...api,selecionar:()=>{}}}><ContextoCatalogo.Provider value={{canais:CANAIS,motivosBloqueio:[],prefixosCepAtendidos:[],restricoes:[]}}>
{montado&&(modo==='ficha'?<BlocoCliente conversa={conversa} integrado/>:<PainelCadastros tela="clientes"/>)}
</ContextoCatalogo.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoRascunhosDosAjustes.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Teste/>);
`)
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 390, height: 844 }, locale: 'pt-BR', reducedMotion: 'reduce' })
const page = await context.newPage(); page.setDefaultTimeout(10000)
const erros = [], posts = []; page.on('pageerror', e => erros.push(e.message))
await context.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture', empresa: { id: 'empresa' }, usuario: { id: 'dona' }, expiraEm: Date.now() + 600000 })))
await context.route('**/api/atendimento/conversas/conversa/cliente', route => { posts.push(route.request().postDataJSON()); return route.fulfill({ status: 400, json: { error: { code: 'VALIDATION_ERROR', message: 'Recusa controlada para manter edição.' } } }) })
await context.route('**/api/clientes?*', route => route.fulfill({ json: { data: [], meta: { total: 0, page: 1, limit: 20 } } }))
await context.route('**/api/atendimento/assistente/cadastro', route => route.fulfill({ json: { data: { conversaId: 'conversa', clienteId: null, vinculoRevisao: 0, campos: [{ campo: 'nome', valor: 'Júlia', evidencia: 'Meu nome é Júlia', mensagemId: 'msg' }, { campo: 'telefone', valor: '11999999999', evidencia: 'telefone11999999999', mensagemId: 'msg' }, { campo: 'email', valor: 'julia@exemplo.com', evidencia: 'email julia@exemplo.com', mensagemId: 'msg' }] } } }))
try {
  await page.goto(`${process.env.CONSOLE_TEST_URL ?? 'http://127.0.0.1:5209'}/.build/2395/ficha-identidade/index.html`)
  await page.getByRole('button', { name: 'Editar Nome do cliente', exact: true }).click()
  const nome = page.getByRole('textbox', { name: 'Nome do cliente', exact: true })
  await nome.fill('Texto preservado')
  await page.evaluate(() => window.trocarPessoa())
  await page.getByRole('button', { name: 'Salvar', exact: true }).click()
  assert.equal(posts.length, 0)
  assert.equal(await nome.inputValue(), 'Texto preservado')
  assert.equal(await page.getByText('O vínculo da conversa mudou. Confira o destino da edição antes de salvar. O texto digitado foi preservado.', { exact: true }).count(), 1)
  assert.equal(await nome.evaluate(el => document.getElementById(el.getAttribute('aria-describedby'))?.textContent), 'O vínculo da conversa mudou. Confira o destino da edição antes de salvar. O texto digitado foi preservado.')
  await page.getByRole('button', { name: 'Conferir destino da edição', exact: true }).click()
  await page.getByRole('dialog').getByText('Pessoa B', { exact: true }).waitFor()
  await page.evaluate(() => window.trocarPessoa())
  assert.equal(await page.getByRole('button', { name: 'Manter texto para esta pessoa' }).isEnabled(), false)
  await page.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await page.evaluate(() => window.remontarFicha())
  await nome.waitFor(); assert.equal(await nome.inputValue(), 'Texto preservado')
  await page.getByRole('button', { name: 'Conferir destino da edição', exact: true }).click()
  await page.getByRole('button', { name: 'Manter texto para esta pessoa' }).click()
  await page.getByRole('button', { name: 'Salvar', exact: true }).click()
  await page.getByText('Recusa controlada para manter edição.', { exact: true }).waitFor()
  assert.equal(posts.length, 1); assert.equal(posts[0].clienteId, 'a'); assert.equal(posts[0].vinculoRevisao, 6); assert.equal(posts[0].nome, 'Texto preservado')
  await page.evaluate(() => window.abrirNovoLead())
  await page.getByRole('button', { name: 'Preencher novo cadastro', exact: true }).click()
  await page.getByRole('button', { name: 'Consultar com IA', exact: true }).click()
  await page.getByText('Meu nome é Júlia', { exact: true }).waitFor()
  assert.equal(await page.getByRole('button', { name: 'Usar este dado', exact: true }).count(), 3)
  await page.getByRole('button', { name: 'Usar este dado', exact: true }).first().click()
  assert.equal(await page.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), 'Júlia')
  assert.equal(await page.getByRole('button', { name: 'Usar este dado', exact: true }).count(), 2)
  await page.screenshot({ path: resolve(pasta, 'novo-sem-vinculo-ia-390.png') })
  await page.evaluate(() => window.trocarPessoa())
  await page.getByRole('button', { name: 'Consultar com IA', exact: true }).waitFor({ state: 'detached' })
  assert.equal(await page.getByRole('button', { name: 'Usar este dado', exact: true }).count(), 0)
  assert.equal(await page.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), 'Júlia')
  assert.deepEqual(erros, [])
  console.log('Ficha real390: A→B/ABA semPOST, texto/snapshot persistem remonte, reconferência explícita envia revisão exibida. Novo sem vínculo consulta IA contextual, aplica campo individual e invalida sugestões ao mudar vínculo.')
} finally { await browser.close() }
