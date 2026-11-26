/* eslint-disable no-console */
// Prova da #3374 (Atendimento 35/36): mede a tela do balcão em 8 condições (claro e escuro x 1440x900 e 1920x1080 x
// conversa aberta e Assistente aberto) e grava a medida em .build/atendimento-35/medidas.json, com um print por condição.
// Mede dentro do Atendimento (fila + conversa + ficha, mais o painel do Assistente quando aberto):
//   a laranjas  botões visíveis com fundo cheio da cor de ação/marca     b espaçamentos  valores distintos de gap/padding/margin
//   c fundos    cores de fundo distintas                                  d fonteMin      menor tamanho de fonte
//   e aninhados cartão (borda + raio + fundo) dentro de outro igual       f contraste     textos com contraste < 4,5
//   g alvos     alvos interativos < 24 px                                 h bordaEsq      borda colorida só à esquerda
// SEMPRE sai 0 gravando a medida (o `rodar-provas.mjs` roda todo prova-*.mjs). Só cobra o alvo com ALVO_35=1
// (laranjas==1 e no máximo 1 em cada estado da conversa (antes de assumir, assumida, com texto), espaçamentos só da escala --e-1..--e-6, fundos <= 3 superfícies + 5 estados, fonte >= 13, e/f/g/h == 0).
// A C15 tira essa chave: o alvo passa a ser cobrado sempre.
//
//   PLAYWRIGHT_MODULE=<pasta do playwright> [ALVO_35=1] node ferramentas/prova-atendimento-35.mjs
import { mkdirSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
// O executor sobe o Vite no modo API, sem dados; esta prova sobe o seu, no modo demonstração (`FONTE_API` falso).
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const raiz = fileURLToPath(new URL('..', import.meta.url))
const pasta = resolve(raiz, '.build/atendimento-35')
mkdirSync(pasta, { recursive: true })
const servidor = await createServer({ root: raiz, logLevel: 'error', server: { host: '127.0.0.1', port: 5797, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })

const ESCALA = [4, 8, 12, 16, 24, 32] // --e-1..--e-6
const ESTADOS_PERMITIDOS = 5 // fundos de estado (sucesso, alerta, erro, info, entregue) somados às 3 superfícies

// Roda no navegador: recebe a escala e devolve as 8 medidas.
function medir({ escala }) {
  const raizes = [document.querySelector('main[aria-label="Atendimento"]'), ...document.querySelectorAll('section[role=dialog]')].filter(Boolean)
  const ctx = document.createElement('canvas').getContext('2d', { willReadFrequently: true })
  ctx.canvas.width = ctx.canvas.height = 1
  const rgba = (css) => {
    ctx.clearRect(0, 0, 1, 1); ctx.fillStyle = '#000'; ctx.fillStyle = css; ctx.fillRect(0, 0, 1, 1)
    const [r, g, b, a] = ctx.getImageData(0, 0, 1, 1).data
    return [r, g, b, a / 255]
  }
  const token = (nome) => { const t = document.createElement('i'); t.style.background = `var(--${nome})`; document.body.append(t); const c = rgba(getComputedStyle(t).backgroundColor); t.remove(); return c }
  const acao = ['acao', 'acao-forte', 'marca', 'marca-forte'].map(token)
  const igual = (a, b) => a.slice(0, 3).every((v, i) => Math.abs(v - b[i]) <= 2)
  const visivel = (e) => {
    const r = e.getBoundingClientRect(); const s = getComputedStyle(e)
    return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none' && Number(s.opacity) > 0
  }
  const svg = new Set(['SCRIPT', 'STYLE', 'PATH', 'CIRCLE', 'RECT', 'LINE', 'POLYLINE', 'POLYGON', 'G', 'DEFS', 'ELLIPSE'])
  const todos = raizes.flatMap((r) => [r, ...r.querySelectorAll('*')]).filter((e) => !svg.has(e.tagName.toUpperCase()) && visivel(e))
  const fundoEfetivo = (e) => { // compõe os fundos dos ancestrais até o branco do documento
    const pilha = []
    for (let n = e; n; n = n.parentElement) { const c = rgba(getComputedStyle(n).backgroundColor); if (c[3] > 0) pilha.push(c); if (c[3] >= 1) break }
    let base = [255, 255, 255, 1]
    for (const c of pilha.reverse()) base = base.map((v, i) => i < 3 ? c[i] * c[3] + v * (1 - c[3]) : 1)
    return base
  }
  const lum = ([r, g, b]) => [r, g, b].map((v) => v / 255).map((v) => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4).reduce((s, v, i) => s + v * [.2126, .7152, .0722][i], 0)
  const descricao = (e) => `${e.tagName.toLowerCase()}${e.getAttribute('aria-label') ? `[${e.getAttribute('aria-label')}]` : ''}.${String(e.className).split(' ')[0].slice(0, 40)} "${(e.textContent ?? '').trim().slice(0, 24)}"`

  const laranjas = todos.filter((e) => (e.tagName === 'BUTTON' || e.getAttribute('role') === 'button') && (() => { const c = rgba(getComputedStyle(e).backgroundColor); return c[3] > 0.9 && acao.some((a) => igual(a, c)) })()).map(descricao)

  // Exceções explícitas (C7 e r1), as únicas: (1) `.sr`, o texto só para leitor de tela, que base.css recorta em 1 px com margem -1 px;
  // (2) `Pilula`, componente compartilhado (`componentes/Pilula.module.css`, fora da lista 1.2 do plano) com padding 3/6 px.
  // (3) a margem lateral do palco com o Assistente encaixado: é a reserva da largura do painel (C9), não espaçamento.
  const excecao = (e, p, v) => e.matches('.sr') || (e.className.toString().includes('_pilula_') && p.startsWith('padding') && (v === 3 || v === 6))
    || (e.matches('#conteudo-principal[data-palco-atendimento]') && (p === 'marginRight' || p === 'marginLeft') && document.documentElement.dataset.assistenteEmpurra)
  const espacos = new Set(); const foraDaEscala = new Set(); const origemFora = {}
  for (const e of todos) {
    const s = getComputedStyle(e)
    // `margin: auto` sai em px no getComputedStyle (valor usado); o Typed OM devolve o valor computado, `auto`.
    const mapa = e.computedStyleMap?.()
    for (const p of ['gap', 'rowGap', 'columnGap', 'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft', 'marginTop', 'marginRight', 'marginBottom', 'marginLeft']) {
      if (p.startsWith('margin') && String(mapa?.get(p.replace(/[A-Z]/g, (l) => `-${l.toLowerCase()}`))) === 'auto') continue
      const v = Math.round(Math.abs(parseFloat(s[p])) * 10) / 10
      if (!(v > 0)) continue
      if (excecao(e, p, v)) continue
      espacos.add(v)
      if (!escala.includes(v)) { foraDaEscala.add(v); (origemFora[v] ??= []).length < 4 && origemFora[v].push(`${p} ${descricao(e)}`) }
    }
  }

  const fundos = new Set(todos.map((e) => rgba(getComputedStyle(e).backgroundColor)).filter((c) => c[3] > 0).map((c) => c.map((v, i) => i < 3 ? Math.round(v) : Math.round(v * 100) / 100).join(',')))

  // C6: toda bolha de saída (escrita pelo atendente ou pelo Assistente) tem o mesmo fundo; o selo diz quem mandou.
  const fundosSaida = new Set(todos.filter((e) => /(^|\s)_?saida_/.test(String(e.className))).map((e) => rgba(getComputedStyle(e).backgroundColor).join(',')))

  const comTexto = todos.filter((e) => [...e.childNodes].some((n) => n.nodeType === 3 && n.textContent.trim()))
  const fontesFora = {}
  for (const e of comTexto.filter((x) => !x.matches('.sr'))) { const f = parseFloat(getComputedStyle(e).fontSize); if (![13, 14, 16, 20].includes(f)) (fontesFora[f] ??= []).length < 12 && fontesFora[f].push(descricao(e)) }
  const fontes = [...new Set(comTexto.filter((e) => !e.matches('.sr')).map((e) => parseFloat(getComputedStyle(e).fontSize)))].sort((x, y) => x - y)
  const fonteMin = Math.min(...comTexto.map((e) => parseFloat(getComputedStyle(e).fontSize)))

  const cartao = (e) => { const s = getComputedStyle(e); return parseFloat(s.borderTopWidth) > 0 && parseFloat(s.borderTopLeftRadius) > 0 && rgba(s.backgroundColor)[3] > 0 }
  const aninhados = todos.filter((e) => { if (!cartao(e)) return false; for (let n = e.parentElement; n; n = n.parentElement) if (cartao(n)) return true; return false }).map(descricao)

  const contraste = []
  for (const e of comTexto) {
    const s = getComputedStyle(e); const f = rgba(s.color); const b = fundoEfetivo(e)
    let op = 1; for (let n = e; n; n = n.parentElement) op *= Number(getComputedStyle(n).opacity)
    const a = f[3] * op; const fm = f.slice(0, 3).map((v, i) => v * a + b[i] * (1 - a))
    const [x, y] = [lum(fm), lum(b)].sort((m, n) => n - m); const razao = (x + .05) / (y + .05)
    if (razao < 4.5) contraste.push(`${razao.toFixed(2)} ${descricao(e)}`)
  }

  const interativo = 'button, a[href], input:not([type=hidden]), select, textarea, [role=button], [role=tab], [role=menuitem], [role=checkbox], [role=switch], [role=slider]'
  const alvos = todos.filter((e) => e.matches(interativo) && getComputedStyle(e).display !== 'inline').filter((e) => { const r = e.getBoundingClientRect(); return (r.width > 1 || r.height > 1) && (r.width < 24 || r.height < 24) }) // 1x1 é campo só para leitor de tela (.sr)
    .map((e) => `${Math.round(e.getBoundingClientRect().width)}x${Math.round(e.getBoundingClientRect().height)} ${descricao(e)}`)

  const bordaEsq = todos.filter((e) => {
    const s = getComputedStyle(e)
    if (parseFloat(s.borderLeftWidth) > 0 && !parseFloat(s.borderTopWidth) && !parseFloat(s.borderRightWidth) && !parseFloat(s.borderBottomWidth) && rgba(s.borderLeftColor)[3] > 0) return true
    for (const ps of ['::before', '::after']) { // listra vertical como pseudo-elemento
      const q = getComputedStyle(e, ps); if (q.content === 'none' || q.position !== 'absolute') continue
      const w = parseFloat(q.width); const h = parseFloat(q.height); const r = e.getBoundingClientRect()
      if (w > 0 && w <= 4 && h >= r.height * 0.5 && rgba(q.backgroundColor)[3] > 0 && parseFloat(q.left) <= 1) return true
    }
    return false
  }).map(descricao)

  return {
    laranjas: { n: laranjas.length, itens: laranjas },
    espacamentos: { n: espacos.size, valores: [...espacos].sort((x, y) => x - y), foraDaEscala: [...foraDaEscala].sort((x, y) => x - y), origem: origemFora },
    fundos: { n: fundos.size, valores: [...fundos] },
    fundosSaida: { n: fundosSaida.size, valores: [...fundosSaida] },
    fonteMin,
    fontes,
    fontesFora,
    aninhados: { n: aninhados.length, itens: aninhados },
    contraste: { n: contraste.length, itens: contraste },
    alvos: { n: alvos.length, itens: alvos },
    bordaEsq: { n: bordaEsq.length, itens: bordaEsq },
  }
}

const fixas = []

// Crítica r1 (sempre cobradas): (a) nenhum botão visível sobre outro, (b) rótulo curto de botão numa linha só e
// (d) nenhum texto do painel do Assistente coberto (pelo rodapé ou por outra camada). Mede só a parte que aparece
// (recortada pelos ancestrais que rolam ou cortam), para texto rolado para fora não contar como coberto.
function layout() {
  const raizes = [document.querySelector('main[aria-label="Atendimento"]'), ...document.querySelectorAll('section[role=dialog]')].filter(Boolean)
  // checkVisibility: o conteúdo de um <details> fechado tem caixa, mas não aparece.
  const visivel = (e) => { const r = e.getBoundingClientRect(); return r.width > 1 && r.height > 1 && e.checkVisibility({ visibilityProperty: true, opacityProperty: true, contentVisibilityAuto: true }) }
  const recorte = (e) => {
    let r = e.getBoundingClientRect(); let [x1, y1, x2, y2] = [r.left, r.top, r.right, r.bottom]
    for (let n = e.parentElement; n; n = n.parentElement) {
      const s = getComputedStyle(n)
      if (s.overflowX === 'visible' && s.overflowY === 'visible') continue
      r = n.getBoundingClientRect(); x1 = Math.max(x1, r.left); y1 = Math.max(y1, r.top); x2 = Math.min(x2, r.right); y2 = Math.min(y2, r.bottom)
    }
    x2 = Math.min(x2, innerWidth); y2 = Math.min(y2, innerHeight)
    return x2 - x1 > 1 && y2 - y1 > 1 ? { x1, y1, x2, y2 } : null
  }
  const nome = (e) => (e.getAttribute('aria-label') || e.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 30)
  const botoes = raizes.flatMap((r) => [...r.querySelectorAll('button')]).filter(visivel).map((e) => ({ e, r: recorte(e) })).filter((b) => b.r)
  const sobrepostos = []
  for (let i = 0; i < botoes.length; i++) for (let j = i + 1; j < botoes.length; j++) {
    const [a, b] = [botoes[i], botoes[j]]
    if (a.e.contains(b.e) || b.e.contains(a.e)) continue
    const w = Math.min(a.r.x2, b.r.x2) - Math.max(a.r.x1, b.r.x1); const h = Math.min(a.r.y2, b.r.y2) - Math.max(a.r.y1, b.r.y1)
    if (w > 1 && h > 1) sobrepostos.push(`"${nome(a.e)}" x "${nome(b.e)}" (${Math.round(w)}x${Math.round(h)})`)
  }
  // Rótulo que vaza da caixa do botão também cai sobre o vizinho ("Nota" sobre "Enviar e assumir" a 1440).
  // Texto cortado com reticência (overflow no caminho até o botão) não vaza: só a parte visível conta.
  const corta = (n, e) => { for (let x = n.parentElement; x; x = x === e ? null : x.parentElement) if (getComputedStyle(x).overflowX !== 'visible') return true; return false }
  for (const { e } of botoes) {
    const caixa = e.getBoundingClientRect()
    for (const n of [...e.querySelectorAll('*'), e].flatMap((x) => [...x.childNodes]).filter((t) => t.nodeType === 3 && t.textContent.trim())) {
      if (!visivel(n.parentElement) || getComputedStyle(n.parentElement).clip !== 'auto' || corta(n, e)) continue
      const faixa = document.createRange(); faixa.selectNodeContents(n); const q = faixa.getBoundingClientRect()
      if (q.left < caixa.left - 1 || q.right > caixa.right + 1) sobrepostos.push(`"${n.textContent.trim().slice(0, 20)}" vaza do botão "${nome(e)}"`)
    }
  }
  const quebrados = []
  for (const { e } of botoes) {
    const texto = (e.textContent ?? '').trim()
    if (!texto || texto.length > 30) continue // cartão da fila é botão com várias linhas de propósito
    for (const n of [...e.querySelectorAll('*'), e].flatMap((x) => [...x.childNodes]).filter((t) => t.nodeType === 3 && t.textContent.trim())) {
      const dono = n.parentElement; if (!visivel(dono) || getComputedStyle(dono).clip !== 'auto') continue
      const faixa = document.createRange(); faixa.selectNodeContents(n)
      const linhas = new Set([...faixa.getClientRects()].filter((q) => q.width > 0).map((q) => Math.round(q.top / 4)))
      if (linhas.size > 1) quebrados.push(`"${texto.slice(0, 30)}" em ${linhas.size} linhas`)
    }
  }
  const cobertos = []
  for (const painel of document.querySelectorAll('section[role=dialog]')) {
    for (const e of painel.querySelectorAll('*')) {
      if (!visivel(e) || e.matches('.sr') || ![...e.childNodes].some((t) => t.nodeType === 3 && t.textContent.trim())) continue
      const r = e.getBoundingClientRect(); const c = recorte(e); if (!c) continue
      const [x, y] = [(r.left + r.right) / 2, (r.top + r.bottom) / 2]
      if (x < c.x1 || x > c.x2 || y < c.y1 || y > c.y2) continue // o centro foi rolado para fora: não está à vista, não está coberto
      const no = document.elementFromPoint(x, y)
      if (no && no !== e && !e.contains(no)) cobertos.push(`"${(e.textContent ?? '').trim().slice(0, 30)}" sob ${no.tagName.toLowerCase()}.${String(no.className).split(' ')[0].slice(0, 30)}`)
    }
  }
  return { sobrepostos, quebrados, cobertos }
}
const cobrarLayout = async (p, nome) => {
  const l = await p.evaluate(layout)
  fixas.push([l.sobrepostos.length === 0, `${nome}: botões sobrepostos: ${l.sobrepostos.slice(0, 4).join(' | ')}`])
  fixas.push([l.quebrados.length === 0, `${nome}: rótulo de botão quebrado: ${l.quebrados.slice(0, 4).join(' | ')}`])
  fixas.push([l.cobertos.length === 0, `${nome}: texto do Assistente coberto: ${l.cobertos.slice(0, 4).join(' | ')}`])
}
const condicoes = []
for (const tema of ['light', 'dark']) for (const [w, h] of [[1440, 900], [1920, 1080]]) for (const cena of ['conversa', 'assistente']) condicoes.push({ tema, w, h, cena })
// Entre 1180 e 1307 px não cabem as três colunas e o painel: ele toma o lugar da ficha e o Assumir segue à vista (crítica r1).
condicoes.push({ tema: 'light', w: 1280, h: 800, cena: 'assistente', estreita: true })

const medidas = []
for (const c of condicoes) {
  const nome = `${c.tema === 'light' ? 'claro' : 'escuro'}-${c.w}-${c.cena}`
  const p = await navegador.newPage({ viewport: { width: c.w, height: c.h } })
  const erros = []
  p.on('pageerror', (e) => erros.push(e.message))
  await p.goto(`${origem}/#/m/atendimento/balcao`, { timeout: 90000 })
  await p.evaluate((t) => { document.documentElement.dataset.theme = t }, c.tema)
  await p.getByRole('button', { name: /^Abrir .+/ }).first().click()
  await p.locator('section[aria-label^="Conversa com"]').waitFor({ timeout: 30000 })
  await p.locator('section[aria-label="Ficha"]').waitFor()
  if (c.cena === 'assistente') {
    await p.getByRole('button', { name: /Abrir assistente/i }).first().click()
    await p.locator('section[role=dialog]').first().waitFor()
    await p.mouse.move(0, 0) // mede em repouso: o cursor parado sobre o botão que abriu o painel contaria o hover como fundo
  }
  await p.waitForTimeout(600)
  const m = await p.evaluate(medir, { escala: ESCALA })
  await cobrarLayout(p, nome)
  await p.screenshot({ path: resolve(pasta, `${nome}.png`) })
  // C3 (um laranja por vez): o mesmo botão cheio muda de dono com o estado. Antes de assumir só `Assumir`; assumida e com
  // campo vazio, nenhum (o microfone ocupa o lugar do Enviar); com texto, só `Enviar`. Mede depois do print, só na conversa.
  let porEstado
  if (c.cena === 'conversa') {
    const contar = async () => { await p.waitForTimeout(300); return (await p.evaluate(medir, { escala: ESCALA })).laranjas }
    const antes = await contar()
    // C10: a tecla A (foco fora de campo) assume pelo mesmo botão, e a dica dela está à vista no botão.
    fixas.push([await p.locator('button[aria-keyshortcuts="A"] kbd', { hasText: /^A$/ }).first().isVisible(), `${nome}: dica "A" invisível no Assumir`])
    await p.evaluate(() => document.activeElement?.blur())
    await p.keyboard.press('a')
    await p.getByRole('button', { name: /^Assumir conversa/ }).waitFor({ state: 'detached', timeout: 5000 }).catch(() => {})
    fixas.push([(await p.getByRole('button', { name: /^Assumir conversa/ }).count()) === 0, `${nome}: a tecla A não assumiu a conversa`])
    const assumida = await contar()
    await p.locator('main[aria-label="Atendimento"] textarea').first().fill('oi')
    const comTexto = await contar()
    await cobrarLayout(p, `${nome} com texto`)
    porEstado = { antes, assumida, comTexto }
  }
  // C9 e C10 (sempre cobradas, são baratas e estáveis): com o Assistente aberto o Assumir fica à vista e clicável (o centro dele cai nele
// e o painel não o cobre) e o Esc fecha o painel devolvendo o foco ao botão que o abriu.
if (c.cena === 'assistente') {
const painelLargura = await p.locator('section[role=dialog]').first().boundingBox().then((b) => b && Math.round(b.width))
fixas.push([painelLargura >= 320, `${nome}: painel com ${painelLargura} px (mínimo 320)`])
const assumir = p.getByRole('button', { name: /^Assumir conversa/ }).first()
const cx = await assumir.boundingBox().then((b) => b && { x: b.x + b.width / 2, y: b.y + b.height / 2 })
const clicavel = cx ? await p.evaluate(({ x, y }) => { const el = document.elementFromPoint(x, y); return !!el?.closest('button')?.textContent.startsWith('Assumir conversa') }, cx) : false
const painel = await p.locator('section[role=dialog]').first().boundingBox()
const sobrepoe = !!(cx && painel && cx.x >= painel.x && cx.x <= painel.x + painel.width && cx.y >= painel.y && cx.y <= painel.y + painel.height)
const conversa = await p.locator('section[aria-label^="Conversa com"]').boundingBox()
const largura = await p.evaluate(() => document.documentElement.clientWidth)
const folga = painel ? { esq: Math.round(painel.x), conversaFim: conversa ? Math.round(conversa.x + conversa.width) : null } : null
fixas.push([clicavel && !sobrepoe, `${nome}: Assumir ${cx ? '' : 'invisível, '}${clicavel ? '' : 'coberto, '}${sobrepoe ? 'sob o painel' : ''}`])
fixas.push([!folga || folga.conversaFim === null || folga.conversaFim <= folga.esq + 1, `${nome}: conversa termina em ${folga?.conversaFim}, painel começa em ${folga?.esq}`])
fixas.push([!(await p.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)), `${nome}: rolagem horizontal da página (${largura} px)`])
fixas.push([await p.locator('section[role=dialog] kbd', { hasText: /^Esc$/ }).first().isVisible(), `${nome}: dica "Esc" invisível no painel`])
await p.keyboard.press('Escape')
await p.waitForTimeout(200)
const fechou = (await p.locator('section[role=dialog]').count()) === 0
const foco = await p.evaluate(() => document.activeElement?.getAttribute('aria-label'))
fixas.push([fechou && foco === 'Abrir assistente', `${nome}: Esc ${fechou ? 'fechou' : 'não fechou'}, foco em "${foco}" (esperado "Abrir assistente")`])
}
medidas.push({ condicao: nome, ...c, ...m, laranjasPorEstado: porEstado, errosDePagina: erros })
  await p.close()
}

writeFileSync(resolve(pasta, 'medidas.json'), `${JSON.stringify({ geradoEm: new Date().toISOString(), alvo: { laranjas: 1, espacamentos: ESCALA, fundosMax: 3 + ESTADOS_PERMITIDOS, fonteMin: 13 }, medidas }, null, 2)}\n`)
await navegador.close()
await servidor.close()

console.log('condição                          a.lar b.esp(fora) c.fundos d.fonte e.anin f.contr g.alvo h.esq')
const cobranca = []
for (const m of medidas) {
  console.log(`${m.condicao.padEnd(32)} ${String(m.laranjas.n).padStart(5)} ${`${m.espacamentos.n}(${m.espacamentos.foraDaEscala.length})`.padStart(10)} ${String(m.fundos.n).padStart(8)} ${String(m.fonteMin).padStart(7)} ${String(m.aninhados.n).padStart(6)} ${String(m.contraste.n).padStart(7)} ${String(m.alvos.n).padStart(6)} ${String(m.bordaEsq.n).padStart(5)}`)
  const falha = (ok, texto) => { if (!ok) cobranca.push(`${m.condicao}: ${texto}`) }
  falha(m.laranjas.n === 1, `laranjas ${m.laranjas.n} != 1`)
  if (m.laranjasPorEstado) for (const [estado, v] of Object.entries(m.laranjasPorEstado)) falha(v.n <= 1, `laranjas ${v.n} > 1 em "${estado}": ${v.itens.join(' | ')}`)
  falha(m.espacamentos.foraDaEscala.length === 0, `espaçamentos fora da escala: ${m.espacamentos.foraDaEscala.join(', ')} | ${Object.entries(m.espacamentos.origem).map(([v, o]) => `${v}: ${o.join('; ')}`).join(' | ')}`)
  falha(m.fundos.n <= 3 + ESTADOS_PERMITIDOS, `fundos ${m.fundos.n} > ${3 + ESTADOS_PERMITIDOS}`)
  falha(m.fundosSaida.n <= 1, `fundos de bolha de saída ${m.fundosSaida.n} > 1`)
  falha(m.fontesFora && Object.keys(m.fontesFora).length === 0, `fontes fora de 13/14/16/20: ${Object.entries(m.fontesFora).map(([f, o]) => `${f}: ${o.slice(0, 4).join('; ')}`).join(' | ')}`)
  falha(m.fonteMin >= 13, `fonte mínima ${m.fonteMin} < 13`)
  falha(m.aninhados.n === 0, `cartões aninhados ${m.aninhados.n}`)
  falha(m.contraste.n === 0, `contraste < 4,5 em ${m.contraste.n}`)
  falha(m.alvos.n === 0, `alvos < 24 px: ${m.alvos.n}`)
  falha(m.bordaEsq.n === 0, `borda só à esquerda: ${m.bordaEsq.n}`)
  falha(m.errosDePagina.length === 0, `erros de página: ${m.errosDePagina.join(' | ')}`)
}
console.log(`Medida gravada em ${resolve(pasta, 'medidas.json')}`)
const quebradas = fixas.filter(([ok]) => !ok).map(([, t]) => t)
if (quebradas.length) { console.log(['', `C9/C10: ${quebradas.length} falha(s)`, ...quebradas.map((l) => `  ${l}`)].join('\n')); process.exit(1) }
console.log(`C9/C10 e r1 ok (${fixas.length} verificações: Assumir à vista com o painel, conversa fora do painel, Esc devolve o foco, botões sem sobreposição e em 1 linha, Assistente sem texto coberto)`)
if (process.env.ALVO_35 === '1') {
  if (cobranca.length) { console.log(`\nALVO_35: ${cobranca.length} alvo(s) não atingido(s)\n${cobranca.map((l) => `  ${l}`).join('\n')}`); process.exit(1) }
  console.log('ALVO_35: todos os alvos atingidos')
} else console.log('Alvo não cobrado (defina ALVO_35=1 para cobrar).')
process.exit(0)
