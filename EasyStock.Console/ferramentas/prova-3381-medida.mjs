/* eslint-disable no-console */
// Medida da #3381 (B1..B4), no Console em modo demonstração. Sai 1 se algum item não for atendido.
// B1, B3 e B4 não reproduziam (a medida abaixo passa sem mudança de código) e ficam como guarda; B2 reproduzia e foi corrigido.
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const raiz = fileURLToPath(new URL('..', import.meta.url))
const servidor = await createServer({ root: raiz, logLevel: 'error', server: { host: '127.0.0.1', port: 5798, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const falhas = []
const cobrar = (ok, msg) => { console.log(ok ? 'ok   ' : 'FALHA', msg); if (!ok) falhas.push(msg) }
const abrir = async (w, h) => {
  const p = await navegador.newPage({ viewport: { width: w, height: h } })
  await p.goto(`${origem}/#/m/atendimento/balcao`, { timeout: 90000 })
  await p.getByRole('button', { name: /^Abrir .+/ }).first().waitFor({ timeout: 30000 })
  await p.waitForTimeout(500)
  return p
}
const SO = process.env.SO?.split(',')
const quer = (k) => !SO || SO.includes(k)

// B1: a nota inteira da ficha é alcançável.
if (quer('B1')) for (const [w, h] of [[1280, 800], [1440, 900], [1280, 600], [1440, 700]]) {
  const p = await abrir(w, h)
  await p.getByRole('button', { name: /^Abrir .+/ }).first().click()
  await p.locator('section[aria-label="Ficha"]').waitFor()
  await p.waitForTimeout(600)
  const r = await p.evaluate(() => {
    const ficha = document.querySelector('section[aria-label="Ficha"]')
    const rolaveis = [...ficha.querySelectorAll('*')].filter((e) => /auto|scroll/.test(getComputedStyle(e).overflowY) && e.scrollHeight > e.clientHeight + 1)
    const rodape = ficha.querySelector('footer, [data-rodape-ficha]')
    const nota = [...ficha.querySelectorAll('*')].reverse().find((e) => e.children.length === 0 && /nota/i.test(e.textContent) )
    return { rolaveis: rolaveis.map((e) => `${e.className}:${e.scrollHeight}/${e.clientHeight}`), rodape: rodape && { cls: String(rodape.className), top: rodape.getBoundingClientRect().top }, fichaBottom: ficha.getBoundingClientRect().bottom }
  })
  const cortes = await p.evaluate(() => [...document.querySelectorAll('section[aria-label="Ficha"] *')].filter((e) => getComputedStyle(e).overflowY === 'hidden' && e.scrollHeight > e.clientHeight + 1 && e.textContent.trim()).map((e) => `${String(e.className).split(' ')[0]}:${e.scrollHeight}/${e.clientHeight} "${e.textContent.trim().slice(0, 30)}"`))
  console.log('B1 info', w, h, JSON.stringify(r), 'cortes', JSON.stringify(cortes))
  // Rola cada contêiner rolável até o fim e confere que o último bloco não fica sob o rodapé.
  const r2 = await p.evaluate(() => {
    const ficha = document.querySelector('section[aria-label="Ficha"]')
    const corpo = [...ficha.querySelectorAll('*')].find((e) => /auto|scroll/.test(getComputedStyle(e).overflowY) && e.scrollHeight > e.clientHeight)
    if (!corpo) return { semRolagem: true }
    corpo.scrollTop = corpo.scrollHeight
    const filhos = [...corpo.children].filter((c) => c.getBoundingClientRect().height > 0)
    const ult = filhos.at(-1)
    const cb = corpo.getBoundingClientRect(); const ub = ult.getBoundingClientRect()
    const cx = ub.x + ub.width / 2; const cy = ub.bottom - 2
    const no = document.elementFromPoint(cx, cy)
    return { corpoBottom: cb.bottom, ultBottom: ub.bottom, cobertoPor: ult.contains(no) ? null : String(no?.className) }
  })
  console.log('B1 fim', w, JSON.stringify(r2))
  const abre = await p.locator('section[aria-label="Ficha"]').getByRole('button', { name: /Ver texto inteiro|Ver mais|Ver todas/ }).count()
  cobrar(cortes.length === 0 || abre > 0, `B1 ${w}x${h}: texto cortado (${cortes.length}) tem botão para abrir inteiro`)
  cobrar(r2.semRolagem || (r2.ultBottom <= r2.corpoBottom + 1 && !r2.cobertoPor), `B1 ${w}x${h}: último bloco da ficha alcançável`)
  await p.close()
}

// B2: o cartão aberto fica inteiro na área visível da fila, mesmo no fim dela.
const rolavelDe = (e) => { for (let n = e.parentElement; n; n = n.parentElement) if (/auto|scroll/.test(getComputedStyle(n).overflowY) && n.scrollHeight > n.clientHeight + 1) return n; return null }
if (quer('B2')) for (const [w, h] of [[1280, 560], [1440, 640]]) {
  const p = await abrir(w, h)
  const r = await p.evaluate(async (fn) => {
    const rolavelDe = new Function(`return ${fn}`)()
    const cartoes = [...document.querySelectorAll('main[aria-label="Atendimento"] button[data-conversa-id]')]
    const cont = cartoes.length ? rolavelDe(cartoes[0]) : null
    if (!cont) return { semRolagem: true, n: cartoes.length, topo: 0, fundo: 0 }
    cont.scrollTop = cont.scrollHeight
    await new Promise((t) => setTimeout(t, 100))
    const cb = cont.getBoundingClientRect()
    // o último cartão que a rolagem deixa meio à mostra (pior caso): clica no pedaço visível dele, como a mão faria
    const alvo = cartoes.filter((c) => { const b = c.getBoundingClientRect(); return b.top < cb.bottom - 8 && b.bottom > cb.bottom }).at(-1) ?? cartoes.at(-1)
    cont.scrollTop = cont.scrollHeight - cont.clientHeight - 40
    const b0 = alvo.getBoundingClientRect(); const x = b0.x + 20; const y = Math.min(b0.bottom - 4, cb.bottom - 4)
    const emPonto = document.elementFromPoint(x, y)?.closest('button[data-conversa-id]')
    emPonto.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await new Promise((t) => setTimeout(t, 900))
    const at = document.querySelector('button[data-conversa-id][aria-current="true"]')
    const c2 = rolavelDe(at) ?? cont
    const a = at.getBoundingClientRect(); const k = c2.getBoundingClientRect()
    return { n: cartoes.length, topo: Math.round(a.top - k.top), fundo: Math.round(k.bottom - a.bottom), altura: Math.round(a.height) }
  }, rolavelDe.toString())
  console.log('B2', w, h, JSON.stringify(r))
  cobrar(r.n > 0 && (r.topo >= 0 && r.fundo >= 0), `B2 ${w}x${h}: cartão aberto inteiro na fila (topo ${r.topo}, fundo ${r.fundo})`)
  await p.close()
}
// B3: o botão flutuante "Simular" não cobre cartão da fila (com a fila rolada ao fim, o pior caso).
if (quer('B3')) for (const [w, h] of [[1024, 600], [1024, 700], [1024, 768], [1280, 720], [1280, 800], [1440, 900]]) {
  const p = await abrir(w, h)
  const r = await p.evaluate(async () => {
    const sim = [...document.querySelectorAll('button')].find((e) => /^Simular/.test(e.getAttribute('aria-label') ?? e.textContent.trim()) && e.getBoundingClientRect().width > 0)
    if (!sim) return { semBotao: true, cobre: [] }
    const cartoes = [...document.querySelectorAll('button[data-conversa-id]')]
    let cont = cartoes[0]?.parentElement
    while (cont && !(/auto|scroll/.test(getComputedStyle(cont).overflowY) && cont.scrollHeight > cont.clientHeight + 1)) cont = cont.parentElement
    if (cont) cont.scrollTop = cont.scrollHeight
    await new Promise((t) => setTimeout(t, 150))
    const s = sim.getBoundingClientRect()
    const cobre = cartoes.filter((c) => { const b = c.getBoundingClientRect(); return b.width > 0 && b.left < s.right && b.right > s.left && b.top < s.bottom && b.bottom > s.top }).map((c) => c.getAttribute('aria-label')?.slice(0, 24))
    return { semBotao: false, btn: [s.left, s.top, s.right, s.bottom].map(Math.round), cobre }
  })
  console.log('B3', w, JSON.stringify(r))
  cobrar(r.cobre.length === 0, `B3 ${w}: Simular cobre ${r.cobre.length} cartão(ões)`)
  await p.close()
}

// B4: "Bancada de teste" do Assistente fica toda à vista: o centro do rótulo cai nele.
if (quer('B4')) for (const [w, h] of [[1024, 600], [1024, 700], [1024, 768], [1280, 720], [1280, 800], [1440, 900]]) {
  const p = await abrir(w, h)
  await p.getByRole('button', { name: /^Abrir .+/ }).first().click()
  await p.getByRole('button', { name: /Abrir assistente/i }).first().click()
  await p.locator('section[role=dialog]').first().waitFor()
  await p.waitForTimeout(500)
  const r = await p.evaluate(async () => {
    const el = [...document.querySelectorAll('summary')].find((e) => /Bancada de teste/.test(e.textContent))
    if (!el) return { achou: false }
    const n0 = el.getBoundingClientRect(); const natural = el.contains(document.elementFromPoint(n0.x + n0.width / 2, n0.y + n0.height / 2)) && n0.bottom <= innerHeight && n0.top >= 0
    for (let n = el.parentElement; n; n = n.parentElement) if (n.scrollHeight > n.clientHeight + 1 && /auto|scroll/.test(getComputedStyle(n).overflowY)) n.scrollTop = n.scrollHeight
    el.scrollIntoView({ block: 'nearest' })
    await new Promise((t) => setTimeout(t, 150))
    const b = el.getBoundingClientRect()
    // área esmaecida (mask-image) por onde o rótulo passa: o que cai nela aparece "meio coberto"
    let esmaecido = 0
    for (let n = el.parentElement; n; n = n.parentElement) { if (getComputedStyle(n).maskImage !== 'none') { const k = n.getBoundingClientRect(); const faixa = parseFloat(getComputedStyle(document.documentElement).fontSize) * 0 + 24; esmaecido = Math.max(esmaecido, Math.max(0, b.bottom - Math.max(b.top, k.bottom - faixa))) } }
    const no = document.elementFromPoint(b.x + b.width / 2, b.y + b.height / 2)
    // o rótulo inteiro, não só o centro: os 4 cantos internos
    const cantos = [[b.x + 2, b.y + 2], [b.right - 2, b.y + 2], [b.x + 2, b.bottom - 2], [b.right - 2, b.bottom - 2]].filter(([x, y]) => { const n = document.elementFromPoint(x, y); return !(n && el.contains(n)) }).length
    return { achou: true, rect: [b.x, b.y, b.right, b.bottom].map(Math.round), vh: innerHeight, centro: !!(no && el.contains(no)), cobertoPor: no && `${no.tagName}.${String(no.className).slice(0, 30)}`, natural, cantosCobertos: cantos, esmaecido: Math.round(esmaecido) }
  })
  console.log('B4', w, JSON.stringify(r))
  cobrar(r.achou && r.centro && r.cantosCobertos === 0 && r.esmaecido === 0, `B4 ${w}: Bancada de teste ${r.achou ? 'coberta por ' + r.cobertoPor + ', ' + r.esmaecido + ' px sob o esmaecido' : 'não existe'}`)
  await p.close()
}
await navegador.close(); await servidor.close()
process.exit(falhas.length ? 1 : 0)
