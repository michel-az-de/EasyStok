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

  const espacos = new Set(); const foraDaEscala = new Set()
  for (const e of todos) {
    const s = getComputedStyle(e)
    // `margin: auto` sai em px no getComputedStyle (valor usado); o Typed OM devolve o valor computado, `auto`.
    const mapa = e.computedStyleMap?.()
    for (const p of ['gap', 'rowGap', 'columnGap', 'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft', 'marginTop', 'marginRight', 'marginBottom', 'marginLeft']) {
      if (p.startsWith('margin') && String(mapa?.get(p.replace(/[A-Z]/g, (l) => `-${l.toLowerCase()}`))) === 'auto') continue
      const v = Math.round(Math.abs(parseFloat(s[p])) * 10) / 10
      if (!(v > 0)) continue
      espacos.add(v); if (!escala.includes(v)) foraDaEscala.add(v)
    }
  }

  const fundos = new Set(todos.map((e) => rgba(getComputedStyle(e).backgroundColor)).filter((c) => c[3] > 0).map((c) => c.map((v, i) => i < 3 ? Math.round(v) : Math.round(v * 100) / 100).join(',')))

  const comTexto = todos.filter((e) => [...e.childNodes].some((n) => n.nodeType === 3 && n.textContent.trim()))
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
    espacamentos: { n: espacos.size, valores: [...espacos].sort((x, y) => x - y), foraDaEscala: [...foraDaEscala].sort((x, y) => x - y) },
    fundos: { n: fundos.size, valores: [...fundos] },
    fonteMin,
    aninhados: { n: aninhados.length, itens: aninhados },
    contraste: { n: contraste.length, itens: contraste },
    alvos: { n: alvos.length, itens: alvos },
    bordaEsq: { n: bordaEsq.length, itens: bordaEsq },
  }
}

const condicoes = []
for (const tema of ['light', 'dark']) for (const [w, h] of [[1440, 900], [1920, 1080]]) for (const cena of ['conversa', 'assistente']) condicoes.push({ tema, w, h, cena })

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
  }
  await p.waitForTimeout(600)
  const m = await p.evaluate(medir, { escala: ESCALA })
  await p.screenshot({ path: resolve(pasta, `${nome}.png`) })
  // C3 (um laranja por vez): o mesmo botão cheio muda de dono com o estado. Antes de assumir só `Assumir`; assumida e com
  // campo vazio, nenhum (o microfone ocupa o lugar do Enviar); com texto, só `Enviar`. Mede depois do print, só na conversa.
  let porEstado
  if (c.cena === 'conversa') {
    const contar = async () => { await p.waitForTimeout(300); return (await p.evaluate(medir, { escala: ESCALA })).laranjas }
    const antes = await contar()
    await p.getByRole('button', { name: /^Assumir conversa/ }).first().click()
    const assumida = await contar()
    await p.locator('main[aria-label="Atendimento"] textarea').first().fill('oi')
    const comTexto = await contar()
    porEstado = { antes, assumida, comTexto }
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
  falha(m.espacamentos.foraDaEscala.length === 0, `espaçamentos fora da escala: ${m.espacamentos.foraDaEscala.join(', ')}`)
  falha(m.fundos.n <= 3 + ESTADOS_PERMITIDOS, `fundos ${m.fundos.n} > ${3 + ESTADOS_PERMITIDOS}`)
  falha(m.fonteMin >= 13, `fonte mínima ${m.fonteMin} < 13`)
  falha(m.aninhados.n === 0, `cartões aninhados ${m.aninhados.n}`)
  falha(m.contraste.n === 0, `contraste < 4,5 em ${m.contraste.n}`)
  falha(m.alvos.n === 0, `alvos < 24 px: ${m.alvos.n}`)
  falha(m.bordaEsq.n === 0, `borda só à esquerda: ${m.bordaEsq.n}`)
  falha(m.errosDePagina.length === 0, `erros de página: ${m.errosDePagina.join(' | ')}`)
}
console.log(`Medida gravada em ${resolve(pasta, 'medidas.json')}`)
if (process.env.ALVO_35 === '1') {
  if (cobranca.length) { console.log(`\nALVO_35: ${cobranca.length} alvo(s) não atingido(s)\n${cobranca.map((l) => `  ${l}`).join('\n')}`); process.exit(1) }
  console.log('ALVO_35: todos os alvos atingidos')
} else console.log('Alvo não cobrado (defina ALVO_35=1 para cobrar).')
process.exit(0)
