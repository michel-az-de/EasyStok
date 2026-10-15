// #1847: Chromium, Console completo em demonstração e componentes reais; nenhuma API/cliente real.
// Rodar após npm run build. PLAYWRIGHT_MODULE pode apontar para o runtime já instalado.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { pathToFileURL, fileURLToPath } from 'node:url'
import { resolve } from 'node:path'
import { build } from 'vite'
import react from '@vitejs/plugin-react'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const raiz = fileURLToPath(new URL('..', import.meta.url)).replaceAll('\\', '/')
const navegador = await chromium.launch({ headless: true })
try {
  const pagina = await navegador.newPage({ viewport: { width: 1440, height: 1000 } })
  pagina.setDefaultTimeout(5000)
  await pagina.goto(pathToFileURL(resolve(raiz, 'dist/index.html')).href)
  await pagina.getByText('Abrir Atendimento', { exact: true }).click()
  await pagina.getByRole('button', { name: /Renan Okamoto/ }).click()
  await pagina.getByRole('button', { name: 'Nota interna', exact: true }).click()
  const texto = 'Nota de Renan preservada ao navegar #1847'
  await pagina.getByRole('textbox', { name: 'Texto da nota' }).fill(texto)
  await pagina.getByRole('button', { name: /Bianca Toledo/ }).click()
  assert.equal(await pagina.getByRole('textbox', { name: 'Texto da nota' }).inputValue(), texto)
  assert.match(await pagina.getByRole('dialog', { name: 'Nota interna' }).innerText(), /continua na ficha de Renan Okamoto/)
  await pagina.getByRole('button', { name: 'Fixar nota', exact: true }).click()
  assert.equal(await pagina.getByText(texto, { exact: true }).count(), 0, 'não gravou na ficha de Bianca')
  await pagina.getByRole('button', { name: /Renan Okamoto/ }).click()
  assert.equal(await pagina.getByText(texto, { exact: true }).count(), 1, 'gravou somente no cliente original')
  console.log('ok Console completo: troca de conversa preserva destinatário e texto')

  // Componentes reais com resposta controlada: trocar de ferramenta durante save não remonta a nota.
  // O cartão recebe a mesma quantidade de notas, mas com o novo id da 21ª nota.
  const entrada = `import React,{useState}from '${raiz}/node_modules/react/index.js';
import{createRoot}from '${raiz}/node_modules/react-dom/client.js';
import{ModalNota}from '${raiz}/src/features/notas/ModalNota.jsx';
import{CartaoDoCliente}from '${raiz}/src/features/ficha-cliente/CartaoDoCliente.jsx';
import{ExtratoDoPedido}from '${raiz}/src/features/ficha-cliente/ExtratoDoPedido.jsx';
import{BlocoCobranca}from '${raiz}/src/features/ficha-cliente/BlocoCobranca.jsx';
function Prova(){const[visivel,mostrar]=useState(true);const[terminou,terminar]=useState(false);const[geracao,mudar]=useState(0);const[caso,proximo]=useState(0);const[cenaCobranca,proximaCobranca]=useState(0);
const pedidos=[{estado:'cancelado',totalPagoApi:0,alocadoApi:0},{estado:'cancelado',totalPagoApi:100,alocadoApi:0},{estado:'cancelado',totalPagoApi:100,alocadoApi:50},{estado:'aguardando',totalPagoApi:100,alocadoApi:50},{estado:'aguardando',totalPagoApi:50,alocadoApi:75},{estado:'pago',totalPagoApi:100,alocadoApi:100}];
const cancelado=cenaCobranca%2===1;const manual=cenaCobranca>1;
const cobranca={id:'cb',valor:15.5,link:manual?null:'https://mp.test/link',meio:'pix',criadaEm:1792065600000,expiraEm:1792067400000,canceladaEm:cancelado?1792065660000:null};
const notas=Array.from({length:20},(_,i)=>({id:'nota'+(i+geracao),criadoEm:i+geracao,texto:'texto'+(i+geracao)}));
return React.createElement(React.Fragment,null,
React.createElement('button',{onClick:()=>mostrar(v=>!v)},'Alternar ferramenta'),
React.createElement('button',{onClick:()=>window.responder()},'Responder gravação'),
React.createElement('button',{onClick:()=>mudar(1)},'Adicionar vigésima primeira nota'),
React.createElement('output',null,terminou?'Gravou':'Aguardando'),
React.createElement('button',{onClick:()=>proximo(c=>c+1)},'Próximo extrato'),
React.createElement('button',{onClick:()=>proximaCobranca(c=>c+1)},'Próxima cobrança'),
React.createElement('section',{'aria-label':'Cobrança em teste'},React.createElement(BlocoCobranca,{pedido:{pedidoId:'p',estado:cancelado?'cancelado':'aguardando',statusApi:cancelado?'cancelado':'aguardando_pagamento',totalApi:31,sinalNaReserva:true,itens:[],cobranca},conversaId:'c',editavel:true,nomeCliente:'Maria',agora:1792065600000})),
React.createElement(ExtratoDoPedido,{pedido:{pedidoId:'pedido',totalApi:100,frete:0,...pedidos[caso]}}),
!terminou&&React.createElement(ModalNota,{visivel,nomeCliente:'Maria',outraConversa:true,aoFechar:()=>terminar(true),aoSalvar:async texto=>{window.textoSalvo=texto;await new Promise(r=>window.responder=r);return true}}),
React.createElement(CartaoDoCliente,{conversa:{id:'cliente-cache',clienteId:'cliente',nome:'Maria',cliente:{pedidos:2,totalGasto:100,notas}},fonteApi:true,aoAbrir:()=>{}}))};createRoot(document.getElementById('root')).render(React.createElement(Prova));`
  const compilado = await build({ root: raiz, configFile: false, logLevel: 'silent', define: { 'process.env.NODE_ENV': '"production"' }, plugins: [react(), {
    name: 'prova-isolada', enforce: 'pre',
    resolveId(id) { if (/(^|\/)contextos(?:\.js)?$/.test(id)) return '\0contextos'; if (id.endsWith('/infra/api/conversasApi')) return '\0api-resumo'; if (id === 'prova' || id.replaceAll('\\', '/').endsWith('/prova')) return '\0prova' },
    load(id) { if (id === '\0prova') return entrada; if (id === '\0contextos') return "export const useAtendimento=()=>({fonteApi:true,fidelidade:{}});export const useCatalogo=()=>({cardapio:[]});export const useAcoes=()=>({})"; if (id === '\0api-resumo') return "export async function obterResumoDoCliente(){window.resumos=(window.resumos??0)+1;return {texto:'Resumo '+window.resumos}}" },
  }], build: { write: false, minify: false, lib: { entry: 'prova', formats: ['iife'], name: 'Prova' } } })
  const arquivos = (Array.isArray(compilado) ? compilado[0] : compilado).output
  const codigo = arquivos.find(a => a.type === 'chunk').code
  const estilo = arquivos.find(a => a.type === 'asset' && a.fileName.endsWith('.css'))?.source
  const componente = await navegador.newPage()
  componente.setDefaultTimeout(5000)
  componente.on('pageerror', erro => console.error('Componente: ' + erro.message))
  await componente.setContent('<div id="root"></div>')
  if (estilo) await componente.addStyleTag({ content: String(estilo) })
  await componente.addScriptTag({ content: codigo })
  await componente.getByText('Resumo 1', { exact: true }).waitFor()
  await componente.getByRole('textbox', { name: 'Texto da nota' }).fill('Texto em gravação')
  await componente.getByRole('button', { name: 'Fixar nota', exact: true }).click()
  await componente.getByRole('button', { name: 'Alternar ferramenta' }).click()
  assert.equal(await componente.locator('dialog').isVisible(), false, 'CSS deve ocultar os pixels, além de remover o papel de diálogo')
  await componente.getByRole('button', { name: 'Alternar ferramenta' }).click()
  assert.equal(await componente.getByRole('textbox', { name: 'Texto da nota' }).inputValue(), 'Texto em gravação')
  assert.equal(await componente.getByRole('button', { name: 'Salvando…' }).isDisabled(), true)
  await componente.getByRole('button', { name: 'Alternar ferramenta' }).click()
  await componente.getByRole('button', { name: 'Responder gravação' }).click()
  await componente.getByText('Gravou', { exact: true }).waitFor()
  assert.equal(await componente.evaluate(() => window.textoSalvo), 'Texto em gravação')
  assert.equal(await componente.getByRole('dialog').count(), 0)
  console.log('ok nota: gravação em voo e rascunho sobrevivem à troca de ferramenta')
  await componente.getByRole('button', { name: 'Adicionar vigésima primeira nota' }).click()
  await componente.getByText('Resumo 2', { exact: true }).waitFor()
  assert.equal(await componente.evaluate(() => window.resumos), 2)
  console.log('ok cache: 21ª nota invalida o resumo apesar do limite de 20')
  const extrato = componente.getByRole('region', { name: 'Extrato do pedido' })
  for (const caso of [0, 1, 2]) {
    const texto = await extrato.innerText()
    assert.match(texto, /Cancelado · nada a cobrar\s+R\$\s*0,00/)
    assert.doesNotMatch(texto, /A receber|Quitado|Devoluções|Cupom e créditos/)
    if (caso > 0) assert.match(texto, /Ajuste de saldo/)
    if (caso === 2) assert.match(texto, /Saldo em pagamentos e créditos\s+R\$\s*50,00/)
    await componente.getByRole('button', { name: 'Próximo extrato' }).click()
  }
  assert.match(await extrato.innerText(), /Ajuste de saldo/)
  assert.doesNotMatch(await extrato.innerText(), /Devoluções/)
  assert.match(await extrato.innerText(), /A receber\s+R\$\s*50,00/)
  await componente.getByRole('button', { name: 'Próximo extrato' }).click()
  assert.match(await extrato.innerText(), /A receber\s+R\$\s*25,00/)
  await componente.getByRole('button', { name: 'Próximo extrato' }).click()
  assert.match(await extrato.innerText(), /Quitado\s+R\$\s*0,00/)
  console.log('ok extrato real: cancelados sem dívida, ajustes neutros e ativos preservados')
  const cobranca = componente.getByRole('region', { name: 'Cobrança em teste' })
  assert.match(await cobranca.innerText(), /Sinal agora: R\$\s*15,50/)
  for (const cena of [1, 2, 3]) {
    await componente.getByRole('button', { name: 'Próxima cobrança' }).click()
    const texto = await cobranca.innerText()
    if (cena === 2) {
      assert.match(texto, /Sinal a receber/)
      assert.equal(await cobranca.getByRole('button', { name: 'Registrar sinal', exact: true }).isVisible(), true)
    } else {
      assert.match(texto, /Cobrança original/)
      assert.doesNotMatch(texto, /Sinal agora|Sinal a receber|Receber na entrega|Restante na entrega|Saldo na entrega/)
      assert.equal(await cobranca.getByRole('button', { name: /Registrar sinal|Marcar pago|Ainda não recebi|Refazer cobrança/ }).count(), 0)
    }
  }
  console.log('ok cobrança real: cancelados preservam histórico sem instruir novo recebimento; ativos mantêm sinal e ação')
} finally { await navegador.close() }
