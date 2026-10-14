import assert from 'node:assert/strict'
import { mkdirSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { resolve, join } from 'node:path'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright')
const base = process.env.CONSOLE_URL || 'http://127.0.0.1:5774'
const lab = resolve(import.meta.dirname, '../.build/sugestao-tab')
const saida = resolve(import.meta.dirname, '../../.build/1757/prints')
mkdirSync(lab, { recursive: true }); mkdirSync(saida, { recursive: true })
writeFileSync(join(lab, 'index.html'), '<html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="/\.build/sugestao-tab/index.jsx"></script></html>')
writeFileSync(join(lab, 'index.jsx'), `
import React,{useState} from 'react';import{createRoot}from'react-dom/client';
import{ContextoEstado,ContextoAcoes,ContextoCatalogo}from'/src/aplicacao/contextos.js';
import{Composer}from'/src/features/atendimento/Composer.jsx';
import{SelecaoCompartilhada}from'/src/features/ficha-cliente/ComandaDoCliente.jsx';
import '/src/estilos/base.css';
const noop=()=>{};const q=new URLSearchParams(location.search);window.envios=[];
function Lab(){const[id,setId]=useState('11111111-1111-4111-8111-111111111111');const[estado,setEstado]=useState({texto:'',escritaDeFora:0});
const acoes={descarregarRascunho:({texto})=>setEstado(e=>({...e,texto})),enviarMidia:noop};
const c={linkId:'link-teste',revisao:1,podeEditar:true,totalPrevisto:35,rascunho:{observacoes:'',itens:[],endereco:{logradouro:'Rua de teste',numero:'1',bairro:'Centro',cidade:'São Paulo',uf:'SP',cep:'01001000'}},selecao:[],pedido:{id:'pedido',total:35,alocado:0},freteCombinado:0};
return <ContextoEstado value={{fonteApi:q.get('demo')!=='1'}}><ContextoAcoes value={acoes}><ContextoCatalogo value={{respostasProntas:[],cardapio:[]}}><main style={{maxWidth:780,margin:'0 auto',padding:16,height:'100dvh',overflowY:'auto'}}><h1>Escrita assistida</h1><button onClick={()=>{setId('22222222-2222-4222-8222-222222222222');setEstado({texto:'',escritaDeFora:1})}}>Trocar conversa</button><Composer canal={{nome:'Chat do site',aceitaMidia:{}}} conversa={{id,nome:'Maria',canal:'ChatSite'}} podeEscrever={q.get('bloqueado')!=='1'} rascunho={estado.texto} escritaDeFora={estado.escritaDeFora} aoEnviar={texto=>window.envios.push(texto)} aoAbrirNota={noop}/><h2>Pedido compartilhado</h2><SelecaoCompartilhada key={id} conversaId={id} comanda={c} ocupado={false} cardapio={[]} salvar={noop}/></main></ContextoCatalogo></ContextoAcoes></ContextoEstado>}
createRoot(document.getElementById('root')).render(<Lab/>);
`)
const browser = await chromium.launch({ headless: true })
const resultados = [], erros = []
async function abrir({ width = 1280, modo = 'normal', query = '' } = {}) {
  const context = await browser.newContext({ viewport: { width, height: 900 }, isMobile: width < 500, hasTouch: width < 500, reducedMotion: 'reduce' })
  await context.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' }, usuario: { id: 'usuario' } })))
  let chamadas = [], pendente
  await context.route(url => url.pathname.startsWith('/api/'), async route => {
    if (!route.request().url().endsWith('/assistente')) return route.fulfill({ json: { success: true, data: [] } })
    const corpo = route.request().postDataJSON(); chamadas.push(corpo)
    const texto = corpo.pergunta.split('\nTRECHO:\n')[1]
    if (modo === 'lento') await new Promise(resolve => { pendente = resolve })
    if (modo === 'erro') return route.fulfill({ status: 503, json: { error: { code: 'IA_OFF', message: 'IA indisponível' } } })
    const rascunho = { tipo: 'rascunho', texto: texto + 'confirmar os detalhes?' }
    const cozinha = corpo.conversaId === null
    const resposta = modo === 'rascunho-invalido' ? '' : !cozinha && modo === 'rascunho' ? '' : modo === 'explicacao' ? 'Preparei uma sugestão.' : modo === 'invalido' ? 'Claro! ' + texto : texto + 'confirmar os detalhes?'
    const acoes = cozinha ? [] : modo === 'rascunho-invalido' ? [{ tipo: 'rascunho', texto: 'Outro prefixo' }, { tipo: 'nota_interna', texto: rascunho.texto }]
      : modo === 'rascunho' || modo === 'explicacao' ? [rascunho] : [{ tipo: 'enviar-mensagem', texto: 'NÃO ENVIAR' }]
    return route.fulfill({ json: { success: true, data: { resposta, acoes } } })
  })
  const page = await context.newPage(); page.on('pageerror', e => erros.push(e.message))
  await page.goto(base + '/.build/sugestao-tab/index.html' + query)
  const campo = page.getByLabel('Mensagem para o cliente', { exact: true })
  return { context, page, campo, chamadas, liberar: () => pendente?.() }
}
async function foto(page, nome) { await page.screenshot({ path: join(saida, nome + '.png'), fullPage: true }); resultados.push(nome) }
const texto = 'Bom dia! Podemos '
try {
  for (const width of [1280,390]) {
    const t = await abrir({ width, modo: 'rascunho' }); await t.campo.fill(texto)
    await t.page.getByRole('button', { name: 'Usar sugestão' }).waitFor()
    assert.equal(t.chamadas.length, 1); assert.equal(t.chamadas[0].conversaId, '11111111-1111-4111-8111-111111111111')
    await foto(t.page, 'composer-sugestao-' + width)
    if (width === 1280) await t.campo.press('Tab'); else await t.page.getByRole('button', { name: 'Usar sugestão' }).click()
    assert.equal(await t.campo.inputValue(), texto + 'confirmar os detalhes?'); assert.deepEqual(await t.page.evaluate(() => window.envios), [])
    await foto(t.page, 'composer-aceita-' + width)
    await t.page.getByText('Observação do pedido', { exact: true }).click()
    const obs = t.page.getByLabel('Observação para a cozinha'); await obs.fill('Para este pedido, ')
    await t.page.getByRole('button', { name: 'Usar sugestão' }).waitFor({ timeout: 12000 })
    assert.equal(t.chamadas.at(-1).conversaId, null, 'cozinha pede apenas redação, sem ferramentas da conversa')
    const usar = t.page.getByRole('button', { name: 'Usar sugestão' })
    await usar.evaluate(el => el.scrollIntoView({ block: 'center' }))
    assert.equal(await usar.evaluate(el => { const r=el.getBoundingClientRect(); return el.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)) }),true,'o botão precisa estar livre da barra fixa')
    await foto(t.page, 'observacao-sugestao-' + width)
    if (width === 390) await usar.click(); else await obs.press('Tab')
    assert.equal(await obs.inputValue(), 'Para este pedido, confirmar os detalhes?')
    await t.context.close()
  }
  for (const modo of ['erro','invalido','rascunho-invalido']) {
    const t = await abrir({ modo }); await t.campo.fill(texto); await t.page.waitForTimeout(1700)
    assert.equal(t.chamadas.length, 1); assert.equal(await t.page.getByRole('button', { name: 'Usar sugestão' }).count(), 0)
    await t.campo.press('Tab'); assert.equal(await t.campo.evaluate(el => el === document.activeElement), false)
    await foto(t.page, modo); await t.context.close()
  }
  const proposta = await abrir({ modo: 'explicacao' }); await proposta.campo.fill(texto)
  await proposta.page.getByRole('button', { name: 'Usar sugestão' }).waitFor()
  await foto(proposta.page, 'explicacao-com-rascunho'); await proposta.campo.press('Tab')
  assert.equal(await proposta.campo.inputValue(), texto + 'confirmar os detalhes?')
  assert.deepEqual(await proposta.page.evaluate(() => window.envios), []); await proposta.context.close()
  const esc = await abrir(); await esc.campo.fill(texto); await esc.page.getByRole('button', { name: 'Usar sugestão' }).waitFor()
  await esc.campo.press('Shift+Tab'); assert.equal(await esc.campo.inputValue(),texto)
  assert.equal(await esc.campo.evaluate(el => el === document.activeElement),false)
  await esc.campo.focus(); await esc.campo.evaluate(el => el.setSelectionRange(1,4)); await esc.campo.press('Tab')
  assert.equal(await esc.campo.inputValue(),texto); assert.equal(await esc.campo.evaluate(el => el === document.activeElement),false)
  await esc.campo.focus(); await esc.campo.evaluate(el => el.setSelectionRange(el.value.length,el.value.length))
  await esc.campo.press('Escape'); assert.equal(await esc.page.getByRole('button', { name: 'Usar sugestão' }).count(), 0)
  await esc.page.waitForTimeout(1500); assert.equal(esc.chamadas.length, 1); await foto(esc.page, 'escape'); await esc.context.close()
  for (const acao of ['trocar','sair','texto']) {
    const t = await abrir({ modo: 'lento' }); await t.campo.fill(texto); await t.page.waitForTimeout(1500); assert.equal(t.chamadas.length, 1)
    if (acao === 'trocar') await t.page.getByRole('button', { name: 'Trocar conversa' }).click()
    if (acao === 'sair') await t.page.evaluate(() => { sessionStorage.removeItem('easystok.sessao'); dispatchEvent(new Event('easystok:sessao-alterada')) })
    if (acao === 'texto') await t.campo.fill('Novo trecho de escrita')
    t.liberar(); await t.page.waitForTimeout(300)
    assert.equal(await t.page.getByRole('button', { name: 'Usar sugestão' }).count(), 0); await foto(t.page,'resposta-obsoleta-' + acao); await t.context.close()
  }
  const demo = await abrir({ query: '?demo=1' }); await demo.campo.fill(texto); await demo.page.waitForTimeout(1500); assert.equal(demo.chamadas.length,0); await demo.context.close()
  assert.deepEqual(erros,[])
  writeFileSync(join(saida,'indice.json'),JSON.stringify({ resultados, erros, evidencia:'React real, IA HTTP controlada; sem chamada real ao modelo ou envio externo.' },null,2))
  console.log('OK #1757:',resultados.length,'prints; teclado/móvel, observação, falha silenciosa, resposta inválida, Escape, troca de conversa/sessão/texto e modo demo.')
} finally { await browser.close() }
