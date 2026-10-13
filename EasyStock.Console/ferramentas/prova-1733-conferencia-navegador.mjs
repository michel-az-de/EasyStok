import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve, join } from 'node:path'
const require=createRequire(import.meta.url)
const {chromium}=require(process.env.PLAYWRIGHT_MODULE??'playwright')
const origem=process.env.CONSOLE_URL??'http://127.0.0.1:5751'
const fixture=resolve(import.meta.dirname,'../.build/conferencia')
const pasta=process.env.EVIDENCIAS_UX??'../.build/conferencia-browser'
mkdirSync(fixture,{recursive:true});mkdirSync(pasta,{recursive:true})
writeFileSync(join(fixture,'index.html'),'<html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="/.build/conferencia/index.jsx"></script></html>')
writeFileSync(join(fixture,'index.jsx'),`
import React,{useState,useRef,useMemo} from 'react';import{createRoot}from'react-dom/client';
import{ContextoEstado,ContextoAcoes,ContextoCatalogo}from'/src/aplicacao/contextos.js';
import{ContextoAcessoModulos}from'/src/aplicacao/acessoModulos.jsx';
import{criarAcoesComandaApi}from'/src/aplicacao/api/comanda.js';import{gravarSessao}from'/src/infra/api/sessao.js';
import{BlocoCobranca}from'/src/features/ficha-cliente/BlocoCobranca.jsx';import'/src/estilos/base.css';
gravarSessao({token:'teste-controlado',expiraEm:Date.now()+60000,empresa:{id:'empresa-controlada'}});
const q=new URLSearchParams(location.search);const modo=q.get('modo');
const inicial={pedidoId:'11111111-1111-1111-1111-111111111111',numero:'1',estado:modo==='cancelado'?'cancelado':'aguardando',statusApi:modo==='cancelado'?'cancelado':'aguardando_pagamento',sinalNaReserva:false,totalApi:100,totalPagoApi:0,itens:[],pagamentos:[],pagamentosApi:[],cobranca:{id:'cb',valor:100,meio:'pix',link:modo==='manual'?null:'https://example.test/pagar',criadaEm:Date.now(),expiraEm:Date.now()+600000}};
function Tela(){const[pedido,setPedido]=useState(inicial);const[aviso,setAviso]=useState(null);const estadoRef=useRef();estadoRef.current={conversas:[{id:'conversa-controlada',pedido}]};
const acoes=useMemo(()=>criarAcoesComandaApi({},{estadoRef,despachar:acao=>{if(acao.pedido)setPedido(acao.pedido);if(acao.mensagem)setAviso(acao.mensagem)}}),[]);
return <ContextoAcessoModulos value={{permite:()=>true,acoes:{editarAtendimento:modo!=='leitura'}}}><ContextoEstado value={{fonteApi:modo!=='demo',fidelidade:{cupons:[]},conversas:[],agora:Date.now()}}><ContextoCatalogo value={{cardapio:[],entregadores:[]}}><ContextoAcoes value={acoes}><main style={{maxWidth:420,margin:'24px auto',padding:16}}>{aviso&&<p role="alert">{aviso}</p>}<BlocoCobranca pedido={pedido} conversaId="conversa-controlada" nomeCliente="Cliente de teste" agora={Date.now()} editavel={modo!=='leitura'}/></main></ContextoAcoes></ContextoCatalogo></ContextoEstado></ContextoAcessoModulos>};createRoot(document.getElementById('root')).render(<Tela/>);
`)
const casos=[
 {nome:'confirmado',confirmado:true,texto:/Pago/},
 {nome:'ja_conferido',confirmado:true,texto:/Pago/},
 {nome:'em_processamento',texto:/ainda está processando/},
 {nome:'nao_encontrado',texto:/ainda não confirmou/},
 {nome:'aguardando_credencial',texto:/Conecte o Mercado Pago/},
 {nome:'nao_conciliado',texto:/não pôde ser confirmado/},
 {nome:'sem_cobranca_online',texto:/Não há cobrança online/},
 {nome:'indisponivel',texto:/Não foi possível conferir/,status:503},
]
const dto={pedidoId:inicialId(),status:'aguardando',total:100,totalPago:100,alocado:100,frete:0,itens:[],pagamentos:[],cobranca:{cobrancaId:'cb',status:'Paga',provedor:'mercadopago',valor:100,valorPago:100,pagaEm:'2026-10-13T16:00:00Z',linkPagamento:'https://example.test/pagar',tentativa:1}}
function inicialId(){return'11111111-1111-1111-1111-111111111111'}
const indice=[];const browser=await chromium.launch({headless:true})
try{for(const width of[1440,390]){
 for(const caso of casos){
  const context=await browser.newContext({viewport:{width,height:960},reducedMotion:'reduce'});await context.grantPermissions(['local-network-access'],{origin:origem});const page=await context.newPage();page.setDefaultTimeout(10000)
  let liberar,avisarEntrada;const entrou=new Promise(resolve=>avisarEntrada=resolve);const espera=new Promise(resolve=>liberar=resolve);const chamadas=[];const erros=[];page.on('pageerror',e=>erros.push(e.message))
  await page.route(url=>url.pathname.startsWith('/api/'),async route=>{const req=route.request();chamadas.push({metodo:req.method(),url:req.url(),body:req.postData()});if(req.url().endsWith('/conferir')){avisarEntrada();await espera;await route.fulfill({status:caso.status??200,json:caso.status?{error:{code:'pagamento_indisponivel',message:'Provedor indisponível'}}:{data:{situacao:caso.nome,confirmado:caso.confirmado??false}}})}else await route.fulfill({json:{data:dto}})})
  const capturar=async etapa=>{const arquivo=caso.nome+'-'+width+'-'+etapa+'.png';await page.screenshot({path:join(pasta,arquivo),fullPage:true});indice.push({caso:caso.nome,width,etapa,arquivo})}
  await page.goto(origem+'/.build/conferencia/index.html');const botao=page.getByRole('button',{name:'Conferir pagamento',exact:true});await botao.waitFor();await capturar('antes');await botao.click();await entrou;await page.getByRole('button',{name:'Conferindo pagamento…',exact:true}).waitFor();assert.equal(await page.getByRole('button',{name:'Conferindo pagamento…',exact:true}).isEnabled(),false);await capturar('consultando');liberar();await page.getByText(caso.texto).first().waitFor();if(caso.confirmado)assert.equal(await page.getByText(/Item novo depois do pagamento/).count(),0);if(caso.status)assert.equal(await page.getByText(/Não foi possível conferir/).count(),1);await capturar('depois');assert.equal(chamadas.filter(x=>x.metodo==='POST').length,1);assert.equal(chamadas[0].body,null);assert.equal(chamadas.some(x=>x.url.endsWith('/pagamentos')||x.url.endsWith('/forma')),false);assert.deepEqual(erros,[]);await context.close();console.log('ok '+caso.nome+'-'+width)
 }
 for(const modo of['manual','cancelado','demo','leitura']){
  const context=await browser.newContext({viewport:{width,height:960}});await context.grantPermissions(['local-network-access'],{origin:origem});const page=await context.newPage();await page.goto(origem+'/.build/conferencia/index.html?modo='+modo);await page.getByText('Cobrança',{exact:true}).waitFor();assert.equal(await page.getByRole('button',{name:'Conferir pagamento',exact:true}).count(),0);const arquivo=modo+'-'+width+'.png';await page.screenshot({path:join(pasta,arquivo),fullPage:true});indice.push({caso:modo,width,arquivo});await context.close();console.log('ok oculto '+modo+'-'+width)
 }
}}finally{await browser.close();writeFileSync(join(pasta,'indice.json'),JSON.stringify(indice,null,2))}
console.log('OK: 24 jornadas e 56 prints; componentes e ações reais com HTTP controlado, sem operação financeira real.')
