import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import assert from 'node:assert/strict'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const fixtures = resolve(process.env.EVIDENCIAS_CHAT_RECEPCAO)
const pasta = resolve(process.env.EVIDENCIAS_UX)
const consoleUrl = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5758'
const siteUrl = process.env.SITE_URL ?? 'http://127.0.0.1:5757'
const historico = JSON.parse(readFileSync(join(fixtures, 'visitante.json'), 'utf8'))
const resumo = JSON.parse(readFileSync(join(fixtures, 'inbox.json'), 'utf8')).data.find(c => c.canal === 'ChatSite')
const pagina = resolve(import.meta.dirname, '../.build/chat-recepcao')
mkdirSync(pagina, { recursive: true }); mkdirSync(pasta, { recursive: true })
writeFileSync(join(pagina, 'inbox.json'), JSON.stringify(resumo))
writeFileSync(join(pagina, 'mensagens.json'), readFileSync(join(fixtures, 'mensagens-console.json')))
writeFileSync(join(pagina, 'index.html'), '<html lang="pt-BR"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="/.build/chat-recepcao/index.jsx"></script></html>')
writeFileSync(join(pagina, 'index.jsx'), `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoCatalogo} from '/src/aplicacao/contextos.js';
import {CANAIS} from '/src/infra/catalogo.js';
import {CartaoConversa} from '/src/features/caixa-de-entrada/CartaoConversa.jsx';
import {conversaDaApi} from '/src/infra/api/traducaoConversas.js'; import '/src/estilos/base.css';
const resumo=await(await fetch('/.build/chat-recepcao/inbox.json')).json();
const mensagens=await(await fetch('/.build/chat-recepcao/mensagens.json')).json();
const conversa=conversaDaApi(resumo,mensagens,{id:'usuario-controlado'},Date.now());
createRoot(document.getElementById('root')).render(<ContextoCatalogo value={{canais:CANAIS,janelas:[],cardapio:[]}}><main style={{maxWidth:600,margin:'24px auto',padding:16}}><CartaoConversa conversa={conversa} agora={Date.now()} aoAbrir={()=>{}}/></main></ContextoCatalogo>);
`)
const falhas = []; const browser = await chromium.launch({ headless: true })
try {
  for (const width of [1440, 390]) {
    const context = await browser.newContext({ viewport: { width, height: 960 }, reducedMotion: 'reduce' })
    try {
      const page = await context.newPage(); const erros = []; page.on('pageerror', e => erros.push(e.message))
      await page.goto(consoleUrl + '/.build/chat-recepcao/index.html'); await page.locator('main button').first().waitFor()
      await page.screenshot({path:join(pasta, 'console-' + width + '.png'),fullPage:true})
      try {
        assert.deepEqual(erros, []); const texto = await page.locator('main').innerText()
        assert.doesNotMatch(texto, /Automático não soube|preferiu não responder/)
        assert.match(texto, /Aguardando equipe[\s\S]*Chat do site/)
        console.log('ok console-' + width)
      } catch (e) { falhas.push('console-' + width + ': ' + e.message.split('\n')[0]) }
      const site = await context.newPage(); const errosSite = []; site.on('pageerror', e => errosSite.push(e.message))
      let liberar; const enviado = new Promise(resolve => { liberar = resolve }); let posts = 0
      const json = (route, data) => route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({data})})
      await site.route('**/api/public/chat/**', async route => {
        const url = new URL(route.request().url())
        if (url.pathname.endsWith('/sessoes')) return json(route, {token:'sessao-controlada-1742-sem-producao',expiraEm:new Date(Date.now()+3600000).toISOString()})
        if (url.pathname.endsWith('/stream')) { await enviado; return route.fulfill({status:200,contentType:'text/event-stream',body:historico.map(m=>'event: mensagem\ndata: '+JSON.stringify(m)+'\n\n').join('')}) }
        if (route.request().method() === 'POST') { posts++; liberar(); return json(route, historico.find(m=>m.doVisitante && m.texto === route.request().postDataJSON().texto)) }
        return json(route, [])
      })
      await site.goto(siteUrl + '/'); await site.waitForFunction(() => Boolean(window.Alpine))
      await site.evaluate(() => window.dispatchEvent(new Event('chat:open')))
      await site.locator('#site-chat-text').fill('Dúvida 1'); await site.getByRole('button',{name:'Enviar mensagem',exact:true}).click()
      await site.getByText('Dúvida 1',{exact:true}).waitFor(); await site.waitForTimeout(300)
      await site.screenshot({path:join(pasta,'site-'+width+'.png')})
      try {
        assert.deepEqual(errosSite, []); assert.equal(posts,1)
        assert.equal(await site.getByText('Recebemos sua mensagem. A equipe da loja responde por aqui.',{exact:true}).count(),1)
        console.log('ok site-' + width)
      } catch(e) { falhas.push('site-'+width+': '+e.message.split('\n')[0]) }
    } finally { await context.close() }
  }
} finally { await browser.close() }
assert.deepEqual(falhas,[],falhas.join('\n'))
console.log('OK: 4 prints, componentes reais site/Console, DTOs HTTP/PG; sessão controlada sem acesso à produção.')
