import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import assert from 'node:assert/strict'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const fixtures = resolve(process.env.EVIDENCIAS_CHAT_COMANDA)
const pasta = resolve(process.env.EVIDENCIAS_UX)
const antes = process.env.PROVA_ANTES === 'true'
const consoleUrl = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5763'
const siteUrl = process.env.SITE_URL ?? 'http://127.0.0.1:5764'
const ler = arquivo => JSON.parse(readFileSync(join(fixtures, arquivo), 'utf8').replace(/^\uFEFF/, ''))
const pagina = resolve(import.meta.dirname, '../.build/chat-comanda')
mkdirSync(pagina, { recursive: true }); mkdirSync(pasta, { recursive: true })
if (!antes) {
  writeFileSync(join(pagina, 'index.html'), '<html lang="pt-BR"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="/.build/chat-comanda/index.jsx"></script></html>')
  writeFileSync(join(pagina, 'index.jsx'), `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoCatalogo} from '/src/aplicacao/contextos.js'; import {CANAIS} from '/src/infra/catalogo.js';
import {CartaoConversa} from '/src/features/caixa-de-entrada/CartaoConversa.jsx';
import {Balao} from '/src/features/atendimento/Balao.jsx';
import {conversaDaApi} from '/src/infra/api/traducaoConversas.js'; import '/src/estilos/base.css';
const marco=new URLSearchParams(location.search).get('marco');
const resumo=await(await fetch('/.build/chat-comanda/'+marco+'-inbox.json')).json();
const mensagens=await(await fetch('/.build/chat-comanda/'+marco+'-console.json')).json();
const conversa=conversaDaApi(resumo,mensagens,{id:'usuario-controlado'},Date.now());
createRoot(document.getElementById('root')).render(<ContextoCatalogo value={{canais:CANAIS,janelas:[],cardapio:[]}}><main style={{maxWidth:600,margin:'24px auto',padding:16}}><CartaoConversa conversa={conversa} agora={Date.now()} aoAbrir={()=>{}}/><section aria-label="Histórico da conversa" style={{display:'flex',flexDirection:'column',gap:16,marginTop:24}}>{conversa.mensagens.map(m=><Balao key={m.id} mensagem={m}/>)}</section></main></ContextoCatalogo>);
`)
}
const browser = await chromium.launch({ headless: true })
try {
  for (const marco of antes ? ['pedido', 'manual'] : ['pedido', 'manual', 'frete']) {
    const historico = ler(marco + '.json').data
    const aviso = historico.find(m => /^(Recebemos seu pedido|O frete combinado é)/.test(m.texto))
    assert.equal(Boolean(aviso), !antes)
    if (!antes) {
      writeFileSync(join(pagina, marco+'-inbox.json'), JSON.stringify(ler(marco+'-inbox.json').data.find(c => c.canal === 'ChatSite')))
      writeFileSync(join(pagina, marco+'-console.json'), JSON.stringify(ler(marco+'-console.json').data))
    }
    for (const width of [1440, 390]) {
      const context = await browser.newContext({ viewport: { width, height: 960 }, reducedMotion: 'reduce' })
      try {
        if (!antes) {
          const page = await context.newPage(); const erros = []; page.on('pageerror', e => erros.push(e.message))
          await page.goto(consoleUrl+'/.build/chat-comanda/index.html?marco='+marco)
          await page.getByText(aviso.texto,{exact:true}).waitFor()
          assert.deepEqual(erros, []); assert.equal(await page.getByText(aviso.texto,{exact:true}).count(), 1)
          assert.match(await page.locator('main').innerText(), /Aguardando equipe/)
          assert.doesNotMatch(await page.locator('main').innerText(), /Automático não soube responder/)
          assert.equal(await page.getByRole('button',{name:'Mensagem automática do sistema'}).count(), 0)
          await page.screenshot({path:join(pasta,'console-'+marco+'-'+width+'.png'),fullPage:true})
        }
        const site = await context.newPage(); const erros = []; site.on('pageerror', e => erros.push(e.message))
        let posts = 0
        await site.route('**/api/public/chat/**', async route => {
          const url = new URL(route.request().url())
          if (url.pathname.endsWith('/sessoes')) return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({data:{token:'sessao-controlada-1748',expiraEm:new Date(Date.now()+3600000).toISOString()}})})
          if (route.request().method() === 'POST') { posts++; return route.abort() }
          if (url.pathname.endsWith('/stream')) return route.fulfill({status:200,contentType:'text/event-stream',body:historico.map(m=>'event: mensagem\ndata: '+JSON.stringify(m)+'\n\n').join('')})
          return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({data:historico})})
        })
        await site.goto(siteUrl+'/'); await site.waitForFunction(() => Boolean(window.Alpine))
        await site.evaluate(() => window.dispatchEvent(new Event('chat:open')))
        await site.getByText('Quero montar meu pedido',{exact:true}).waitFor()
        if (aviso) { await site.getByText(aviso.texto,{exact:true}).waitFor(); assert.equal(await site.getByText(aviso.texto,{exact:true}).count(),1) }
        else assert.doesNotMatch(await site.locator('.site-chat-messages').innerText(), /Recebemos seu pedido|O frete combinado é/)
        assert.deepEqual(erros, []); assert.equal(posts,0)
        const texto = await site.locator('.site-chat-messages').innerText()
        assert.doesNotMatch(texto,/Pedido montado pelo cliente|Frete pendente de decisão humana/)
        assert.equal(await site.evaluate(() => document.documentElement.scrollWidth > innerWidth),false)
        await site.screenshot({path:join(pasta,'site-'+marco+'-'+width+'.png')})
        console.log('ok '+(antes?'antes ':'depois ')+marco+' '+width)
      } finally { await context.close() }
    }
  }
} finally { await browser.close() }
console.log('OK: componentes reais com DTOs HTTP/PG, sem envio externo; link preservado como texto no widget existente.')
