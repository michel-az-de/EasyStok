// Aviso e CTA reais; tela controlada sem pedido, mensagem, cobrança ou API externos.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2395/dispositivo')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React,{useEffect,useState} from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes,ContextoCatalogo} from '../../../src/aplicacao/contextos';
import {AvisosDoDispositivo} from '../../../src/app/AvisosDoDispositivo';
import {BarraProximoPasso} from '../../../src/features/ficha-cliente/BarraProximoPasso';
import '../../../src/estilos/base.css';
const pedido={estado:'aguardando',itens:[{sku:'prato',qtd:1,obs:''}],janela:'janela|2026-11-13',meio:null,pagamentos:[]};
window.montagens=0;
function Tela(){const [texto,digitar]=useState('');useEffect(()=>{window.montagens++},[]);
return <main style={{height:'100%',minHeight:0,overflow:'auto',display:'flex',flexDirection:'column',padding:12,boxSizing:'border-box'}}>
<label>Mensagem preservada<input value={texto} onChange={e=>digitar(e.target.value)}/></label>
<div style={{marginTop:'auto'}}><BarraProximoPasso pedido={pedido} conversaId="conversa" agora={Date.now()} nomeCliente="Cliente de teste" aoGerarPix={()=>{throw Error('Esta prova não gera cobrança')}}/></div>
</main>}
createRoot(document.getElementById('root')).render(<ContextoEstado.Provider value={{fonteApi:true,conversas:[],som:true,audioBloqueado:true,permissaoNotificacao:'denied'}}><ContextoAcoes.Provider value={{definirTroco:()=>{}}}><ContextoCatalogo.Provider value={{entregadores:[],cardapio:[{sku:'prato',nome:'Lasanha',preco:37}],meiosDePagamento:[]}}><AvisosDoDispositivo><Tela/></AvisosDoDispositivo></ContextoCatalogo.Provider></ContextoAcoes.Provider></ContextoEstado.Provider>);
`)
const browser = await chromium.launch({ headless: true })
const resultados = []
try {
  for (const largura of [390, 1440]) {
    const contexto = await browser.newContext({ viewport: { width: largura, height: 900 }, hasTouch: largura === 390, isMobile: largura === 390, locale: 'pt-BR', reducedMotion: 'reduce' })
    const pagina = await contexto.newPage(), erros = []
    pagina.on('pageerror', e => erros.push(e.message))
    await pagina.goto(`${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2395/dispositivo/index.html`)
    const campo = pagina.getByRole('textbox', { name: 'Mensagem preservada' })
    await campo.fill('Rascunho que não pode sumir')
    const gerar = pagina.getByRole('button', { name: 'Gerar cobrança e enviar', exact: true })
    const dispensar = pagina.getByRole('button', { name: 'Dispensar avisos do aparelho' })
    const [cta, aviso] = await Promise.all([gerar.boundingBox(), dispensar.locator('..').boundingBox()])
    assert.ok(cta.y + cta.height <= aviso.y, 'Aviso reserva espaço, sem cobrir CTA')
    if (largura === 390) await gerar.tap(); else await gerar.click()
    await pagina.getByText('Como o cliente vai pagar?', { exact: true }).waitFor()
    await pagina.screenshot({ path: resolve(pasta, `cta-com-aviso-${largura}.png`), fullPage: true })
    if (largura === 390) await dispensar.tap(); else await dispensar.click()
    assert.equal(await dispensar.count(), 0)
    assert.equal(await campo.inputValue(), 'Rascunho que não pode sumir')
    assert.equal(await pagina.evaluate(() => window.montagens), 1, 'Dispensar não remonta a tela')
    assert.deepEqual(erros, [])
    resultados.push({ largura, ctaAcessivelSemDispensar: true, rascunhoPreservado: true, montagens: 1 })
    await contexto.close()
  }
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify(resultados, null, 2))
  console.log('Aviso+CTA reais: toque390 e desktop1440 sem dispensar/force; dismiss preserva rascunho e montagem única.')
} finally { await browser.close() }
