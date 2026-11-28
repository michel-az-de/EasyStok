/* eslint-disable no-console */
// #3386 (crítica r5, P2): layout do Atendimento medido no navegador, modo demonstração.
// Itens: 3 sinal de prazo igual em toda largura, 4 rodapé da Ficha não cobre linhas, 6 Assistente no tablet,
// 8 anel/ícone/dicas, 9 rótulo do painel inteiro, 10 aviso de "sem resposta" à vista. ITENS=3,4 roda só alguns.
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const raiz = fileURLToPath(new URL('..', import.meta.url))
const servidor = await createServer({ root: raiz, logLevel: 'error', server: { host: '127.0.0.1', port: 5806, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const falhas = []
const itens = (process.env.ITENS ?? '3,4,6,8,9,10,11').split(',')
const cobrar = (ok, msg) => { console.log(ok ? 'ok   ' : 'FALHA', msg); if (!ok) falhas.push(msg) }

async function pagina(w, h, tema = 'light') {
  const p = await navegador.newPage({ viewport: { width: w, height: h } })
  await p.emulateMedia({ colorScheme: tema })
  await p.goto(`${origem}/#/m/atendimento/balcao`, { timeout: 90000 })
  await p.getByRole('button', { name: /^Abrir .+/ }).first().waitFor({ timeout: 30000 })
  return p
}
const abrir = async (p, nome) => {
  if (nome) await p.locator('button[data-conversa-id]').filter({ has: p.locator(`text=${nome}`) }).first().click()
  else await p.getByRole('button', { name: /^Abrir .+/ }).first().click()
  await p.waitForTimeout(500)
}
const painelAberto = (p) => p.locator('section[role="dialog"]').first()
async function abrirAssistente(p) {
  await p.getByRole('button', { name: 'Abrir assistente' }).first().click()
  await painelAberto(p).waitFor({ timeout: 5000 })
  await p.waitForTimeout(500)
}
// Procura a conversa que mostra o aviso "N mensagens sem resposta" no painel e deixa o painel aberto nela.
async function comAviso(p) {
  const n = await p.getByRole('button', { name: /^Abrir .+/ }).count()
  for (let i = 0; i < Math.min(n, 12); i++) {
    await p.getByRole('button', { name: /^Abrir .+/ }).nth(i).click()
    await p.waitForTimeout(300)
    await abrirAssistente(p)
    if (await p.locator('section[role="dialog"]').getByText(/sem resposta nesta conversa/i).first().isVisible().catch(() => false)) return true
    await p.keyboard.press('Escape')
    await p.waitForTimeout(300)
  }
  return false
}
const contraste = (p) => p.evaluate(() => {
  const el = [...document.querySelectorAll('section[role="dialog"] p')].find((x) => /sem resposta nesta conversa/i.test(x.textContent))
  const pixel = (cor) => { const c = document.createElement('canvas'); c.width = c.height = 1; const g = c.getContext('2d'); g.fillStyle = cor; g.fillRect(0, 0, 1, 1); return [...g.getImageData(0, 0, 1, 1).data].slice(0, 3) }
  const lum = ([r, g, b]) => { const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4 }; return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b) }
  let fundo = el; let bg = 'rgba(0, 0, 0, 0)'
  while (fundo && /rgba\(0, 0, 0, 0\)|transparent/.test(bg = getComputedStyle(fundo).backgroundColor)) fundo = fundo.parentElement
  const a = lum(pixel(getComputedStyle(el).color)); const b = lum(pixel(bg))
  return Math.round(((Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)) * 100) / 100
})

try {
  if (itens.includes('3')) {
    // Mesma conversa, mesmo estado, mesmo instante: só a largura muda.
    const p = await pagina(1440, 900)
    const ler = () => p.evaluate(() => {
      const c = [...document.querySelectorAll('button[data-conversa-id]')].find((b) => /Bianca/.test(b.getAttribute('aria-label')))
      if (!c) return null
      const t = c.querySelector('time')
      return { relogio: Boolean(c.querySelector('[aria-label="Prazo de resposta estourado"]')), cor: getComputedStyle(t).color, rotulo: /estourad/i.test(c.getAttribute('aria-label')) }
    })
    const larga = await ler()
    await p.setViewportSize({ width: 1024, height: 768 }); await p.waitForTimeout(500)
    const estreita = await ler()
    console.log('item 3 medida', JSON.stringify({ larga, estreita }))
    cobrar(larga && estreita && larga.relogio === estreita.relogio && larga.cor === estreita.cor, 'item 3: sinal de prazo de Bianca igual a 1440 e 1024')
    await p.close()
  }
  if (itens.includes('4')) {
    for (const [w, h] of [[1280, 720], [1280, 800]]) {
      for (const nome of ['Sandra', 'Marcelo', 'Bianca']) {
        const p = await pagina(w, h)
        if (nome === 'Sandra') console.log('conversas', JSON.stringify(await p.locator('button[data-conversa-id]').evaluateAll((l) => l.map((b) => b.querySelector('strong')?.textContent.trim()))))
        await abrir(p, nome).catch(() => console.log('sem conversa', nome))
        const r = await p.evaluate(() => {
          const ficha = document.querySelector('section[aria-label="Ficha"]')
          if (!ficha) return null
          const rol = [...ficha.querySelectorAll('*')].filter((e) => /auto|scroll/.test(getComputedStyle(e).overflowY) && e.scrollHeight > e.clientHeight + 1).sort((a, b) => b.scrollHeight - a.scrollHeight)[0]
          const barra = [...ficha.querySelectorAll('*')].find((e) => e.querySelector('button') && /Ver etapas/.test(e.textContent) && e.children.length <= 4 && e.getBoundingClientRect().height < 200)
          if (!rol) return { semRolagem: true }
          rol.scrollTop = rol.scrollHeight
          const ult = [...rol.children].filter((e) => e.getBoundingClientRect().height > 0).pop()
          const topoBarra = barra ? barra.getBoundingClientRect().top : Infinity
          return { ultimoFundo: Math.round(ult.getBoundingClientRect().bottom), topoBarra: Math.round(topoBarra), rolBaixo: Math.round(rol.getBoundingClientRect().bottom) }
        })
        console.log('item 4 medida', w, h, nome, JSON.stringify(r))
        cobrar(r && (r.semRolagem || r.ultimoFundo <= r.topoBarra + 1), `item 4: ${w}x${h} ${nome}: última linha da ficha acima do rodapé`)
        await p.close()
      }
    }
  }
  if (itens.includes('6')) {
    for (const tema of ['light', 'dark']) {
      const p = await pagina(1024, 768, tema)
      const achou = await comAviso(p)
      cobrar(achou, `item 6: ${tema}: achei conversa com o aviso`)
      const topo = await p.evaluate(() => ({ painel: Math.round([...document.querySelectorAll('section[role="dialog"]')].find((s) => s.getBoundingClientRect().width > 0).getBoundingClientRect().top), palco: Math.round(document.querySelector('#conteudo-principal').getBoundingClientRect().top) }))
      console.log('item 6 medida', tema, JSON.stringify(topo))
      cobrar(topo.painel >= topo.palco, `item 6: ${tema} 1024: topo do painel (${topo.painel}) abaixo do topo do palco (${topo.palco})`)
      if (achou) { const c = await contraste(p); cobrar(c >= 4.5, `item 6: ${tema}: "sem resposta" com contraste ${c}:1 (mín. 4,5)`) }
      await p.close()
    }
  }
  if (itens.includes('8')) {
    const p = await pagina(1440, 900)
    // Só vale conversa ainda não assumida (com o botão "Assumir conversa"): assumida, o campo já tem o foco da loja.
    const total = await p.locator('button[data-conversa-id]').count()
    let naoAssumida = false
    for (let i = 0; i < total && !naoAssumida; i++) {
      await p.locator('button[data-conversa-id]').nth(i).click(); await p.waitForTimeout(300)
      naoAssumida = await p.getByRole('button', { name: /^Assumir conversa/ }).first().isVisible().catch(() => false)
    }
    cobrar(naoAssumida, 'item 8a: achei conversa não assumida para medir')
    const anelDoCampo = () => p.evaluate(() => {
      const t = document.querySelector('textarea'); if (!t) return 'sem-campo'
      if (![...document.querySelectorAll('button')].some((x) => /^Assumir conversa/.test(x.textContent.trim()))) return 'ja-assumida'
      for (let e = t.parentElement, n = 0; e && n < 4; e = e.parentElement, n++) { const s = getComputedStyle(e); if (s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) > 0) return s.outlineColor }
      return null
    })
    const marca = await p.evaluate(() => { const d = document.createElement('i'); d.style.color = 'var(--marca)'; document.body.append(d); const c = getComputedStyle(d).color; d.remove(); return c })
    const aoAbrir = await anelDoCampo()
    await p.locator('textarea').first().click({ force: true }); await p.waitForTimeout(300)
    const aoFocar = await anelDoCampo()
    console.log('item 8a medida', JSON.stringify({ aoAbrir, aoFocar }))
    cobrar(naoAssumida && aoAbrir !== 'ja-assumida' && aoAbrir !== marca && aoFocar !== marca, `item 8a: antes de assumir o campo não ganha o anel laranja (${marca})`)
    await p.close()
    const q = await pagina(1440, 900)
    const achou = await comAviso(q)
    const info = await q.evaluate(() => {
      const d = [...document.querySelectorAll('section[role="dialog"] button[aria-label^="Dispensar"]')]
      return { dispensar: d.length, check: d.filter((b) => b.querySelector('svg.lucide-check, svg[class*="check"]')).length }
    })
    console.log('item 8b medida', achou, JSON.stringify(info))
    cobrar(info.dispensar > 0 && info.check === 0, `item 8b: Dispensar sem ícone de check (${info.check} de ${info.dispensar})`)
    const pos = q.locator('section[role="dialog"] button[aria-label="Encaixar à esquerda"], section[role="dialog"] button[aria-label="Flutuante"]').first()
    await pos.hover(); await q.waitForTimeout(900)
    const dica = await q.locator('[role="tooltip"]').filter({ hasText: /Encaixar|Flutuante/ }).first().isVisible().catch(() => false)
    cobrar(dica, 'item 8c: o ícone de posição mostra dica ao passar o mouse')
    await q.close()
  }
  if (itens.includes('9') || itens.includes('10')) {
    for (const [w, h] of [[1280, 720], [1440, 900]]) {
      const p = await pagina(w, h)
      const achou = await comAviso(p)
      const r = await p.evaluate(() => {
        const dlg = [...document.querySelectorAll('section[role="dialog"]')].find((s) => s.getBoundingClientRect().width > 0)
        const corpo = [...dlg.querySelectorAll('div')].find((d) => /auto|scroll/.test(getComputedStyle(d).overflowY))
        corpo.scrollTop = 0
        const cr = corpo.getBoundingClientRect(); const fade = parseFloat(getComputedStyle(corpo).paddingBottom)
        const caixa = dlg.querySelector('form')?.getBoundingClientRect()
        const m = (re, sel) => { const e = [...dlg.querySelectorAll(sel)].find((x) => re.test(x.textContent)); if (!e) return null; const b = e.getBoundingClientRect(); return { topo: Math.round(b.top), fundo: Math.round(b.bottom), dentro: corpo.contains(e) } }
        return { dlgFundo: Math.round(dlg.getBoundingClientRect().bottom), corpoTopo: Math.round(cr.top), corpoFundo: Math.round(cr.bottom), fade, formTopo: caixa && Math.round(caixa.top), rotulo: m(/^Pergunte ao assistente$/, 'p'), aviso: m(/sem resposta nesta conversa/i, 'p') }
      })
      console.log('item 9/10 medida', w, h, achou, JSON.stringify(r))
      if (itens.includes('9')) cobrar(r.rotulo && (r.rotulo.dentro ? r.rotulo.fundo <= r.corpoFundo - r.fade + 1 : r.rotulo.topo >= r.corpoFundo - 1 && r.rotulo.fundo <= r.dlgFundo), `item 9: ${w}x${h}: "Pergunte ao assistente" inteiro à vista`)
      if (itens.includes('10')) cobrar(achou && r.aviso && r.aviso.fundo <= r.corpoFundo - r.fade + 1, `item 10: ${w}x${h}: aviso "sem resposta" à vista sem rolar`)
      await p.close()
    }
  }
  if (itens.includes('9')) {
    // Celular: o rótulo "Pergunte ao assistente" inteiro à vista, dentro da tela de 390 x 844.
    const p = await pagina(390, 844)
    await p.locator('button[data-conversa-id]').first().click(); await p.waitForTimeout(500)
    await abrirAssistente(p)
    const r = await p.evaluate(() => {
      const e = [...document.querySelectorAll('p')].find((x) => /^Pergunte ao assistente$/.test(x.textContent.trim()) && x.getBoundingClientRect().width > 0)
      if (!e) return null
      const b = e.getBoundingClientRect()
      return { topo: Math.round(b.top), fundo: Math.round(b.bottom), esq: Math.round(b.left), dir: Math.round(b.right), alto: innerHeight, largo: innerWidth }
    })
    console.log('item 9 medida 390', JSON.stringify(r))
    cobrar(r && r.topo >= 0 && r.fundo <= r.alto && r.esq >= 0 && r.dir <= r.largo, 'item 9: 390x844: "Pergunte ao assistente" inteiro à vista')
    await p.close()
  }
  if (itens.includes('11')) {
    // Tablet: o painel acompanha o topo do palco quando um elemento acima dele muda de altura (sem trocar de conversa).
    const p = await pagina(1024, 768)
    await abrir(p)
    await abrirAssistente(p)
    const medir = () => p.evaluate(() => ({
      painel: Math.round([...document.querySelectorAll('section[role="dialog"]')].find((x) => x.getBoundingClientRect().width > 0).getBoundingClientRect().top),
      palco: Math.round(document.querySelector('#conteudo-principal').getBoundingClientRect().top),
    }))
    const antes = await medir()
    await p.evaluate(() => {
      const palco = document.querySelector('#conteudo-principal')
      const faixa = document.createElement('div'); faixa.id = 'faixa-da-prova'; faixa.style.height = '48px'
      palco.parentElement.insertBefore(faixa, palco)
    })
    await p.waitForTimeout(500)
    const depois = await medir()
    console.log('item 11 medida', JSON.stringify({ antes, depois }))
    cobrar(depois.palco !== antes.palco, 'item 11: a faixa de prova moveu o topo do palco')
    cobrar(Math.abs(depois.painel - depois.palco) <= 1 || depois.painel >= depois.palco, `item 11: painel (${depois.painel}) acompanha o topo do palco (${depois.palco})`)
    await p.setViewportSize({ width: 1000, height: 768 }); await p.waitForTimeout(500)
    await p.evaluate(() => { document.querySelector('#faixa-da-prova').style.height = '96px' })
    await p.waitForTimeout(500)
    const fim = await medir()
    cobrar(fim.painel >= fim.palco, `item 11: depois de redimensionar a janela, painel (${fim.painel}) abaixo do topo do palco (${fim.palco})`)
    await p.close()
  }
} finally {
  await navegador.close()
  await servidor.close()
}
if (falhas.length) { console.log(`prova-3386-layout: ${falhas.length} falha(s)`); process.exit(1) }
console.log('prova-3386-layout: ok')
