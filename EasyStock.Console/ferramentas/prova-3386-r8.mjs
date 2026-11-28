/* eslint-disable no-console */
// #3386 (crítica r8): provas renderizadas do Atendimento em 1024, 1280 e 1440 px, claro e escuro.
// A: a fila mostra até duas fileiras de chips inteiros, com o estado que pede ação primeiro (Diz que pagou, Pix vencido).
// B: o cabeçalho do Assistente fica numa linha, com X e título inteiros. C: os ícones de posição têm nome e dica.
// D: o alerta de restrição nunca termina em reticências: ou cabe inteiro ou mostra só "Sem lactose".
// Uso: node ferramentas/prova-3386-r8.mjs [A|B|C|D ...]  (sem argumento roda A e B)
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
// #3398: sem argumento roda só A e B; C roda em prova-3386-r8-parte2.mjs e D em prova-3386-r8-parte3.mjs. Inteira, a prova
// levava 142 s no runner do CI com 4 em paralelo (master 3bffd79de) e estourou o teto de 180 s do rodar-provas.
const pedidos = process.argv.slice(2).map((s) => s.toUpperCase())
const itens = new Set(pedidos.length ? pedidos : ['A', 'B'])
const roda = (i) => itens.has(i)
const raiz = fileURLToPath(new URL('..', import.meta.url))
const servidor = await createServer({ root: raiz, logLevel: 'error', server: { host: '127.0.0.1', port: 5823, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const falhas = []
const cobrar = (ok, msg) => { console.log(ok ? 'ok   ' : 'FALHA', msg); if (!ok) falhas.push(msg) }
const LARGURAS = [1024, 1280, 1440]
const TEMAS = ['light', 'dark']
const ALTURA = { 1024: 768, 1280: 800, 1440: 900 }

async function abrir(w, tema) {
  const p = await navegador.newPage({ viewport: { width: w, height: ALTURA[w] } })
  await p.emulateMedia({ colorScheme: tema })
  await p.goto(`${origem}/#/m/atendimento/balcao`, { timeout: 90000 })
  await p.locator('button[data-conversa-id]').first().waitFor({ timeout: 30000 })
  await p.waitForTimeout(700)
  return p
}
async function abrirConversa(p, nome) {
  await p.locator('button[data-conversa-id]', { hasText: nome }).first().click()
  await p.waitForTimeout(700)
}
async function abrirAssistente(p) {
  await abrirConversa(p, 'Marcelo')
  await p.getByRole('button', { name: /Abrir assistente/i }).first().click()
  await p.waitForTimeout(800)
}

const medirLinhas = (p) => p.evaluate(() => [...document.querySelectorAll('button[data-conversa-id]')].map((b) => {
  const linha = b.querySelector('[class*=contextoLinha]')
  if (!linha) return null
  const lr = linha.getBoundingClientRect()
  const br = b.getBoundingClientRect()
  const chips = [...linha.children].map((c) => {
    const r = c.getBoundingClientRect()
    const visivel = r.width > 0 && r.top >= lr.top - 1 && r.bottom <= lr.bottom + 1
    const parcial = r.width > 0 && !visivel && r.top < lr.bottom - 1 && r.bottom > lr.top + 1
    const t = c.querySelector('[class*=texto]') ?? c
    const cortado = (visivel || parcial) && (parcial || r.right > lr.right + 1 || r.right > br.right + 1 || r.bottom > br.bottom + 1 || c.scrollWidth > c.clientWidth + 1 || t.scrollWidth > t.clientWidth + 1)
    return { texto: c.textContent.trim(), visivel, cortado, topo: Math.round(r.top) }
  })
  const fileiras = new Set(chips.filter((c) => c.visivel).map((c) => c.topo)).size
  return { nome: b.querySelector('strong')?.textContent.trim(), chips, fileiras }
}).filter(Boolean))

try {
  if (roda('A')) {
    for (const tema of TEMAS) for (const w of LARGURAS) {
      const p = await abrir(w, tema)
      const linhas = await medirLinhas(p)
      const cortados = linhas.flatMap((l) => l.chips.filter((c) => c.cortado).map((c) => `${l.nome}: ${c.texto}`))
      cobrar(linhas.length > 5 && cortados.length === 0, `${w} px ${tema}: nenhum chip da fila cortado${cortados.length ? ` (${cortados.join('; ')})` : ''}`)
      const mais2 = linhas.filter((l) => l.fileiras > 2).map((l) => l.nome)
      cobrar(mais2.length === 0, `${w} px ${tema}: no máximo duas fileiras visíveis${mais2.length ? ` (${mais2.join(', ')})` : ''}`)
      for (const [nome, chip] of [['Sandra', 'Pix vencido'], ['Eduarda', 'Diz que pagou'], ['Aline', 'Diz que pagou']]) {
        const l = linhas.find((x) => x.nome?.startsWith(nome))
        const c = l?.chips.find((x) => x.texto.includes(chip))
        cobrar(Boolean(c?.visivel && !c.cortado), `${w} px ${tema}: ${nome} mostra "${chip}" inteiro (${c ? JSON.stringify(c) : 'chip não existe'})`)
      }
      const s = linhas.find((x) => x.nome?.startsWith('Sandra'))
      const iEstado = s?.chips.findIndex((c) => c.texto.includes('Pix vencido')) ?? -1
      const iDinheiro = s?.chips.findIndex((c) => /R\$/.test(c.texto)) ?? -1
      cobrar(iEstado >= 0 && (iDinheiro < 0 || iEstado < iDinheiro), `${w} px ${tema}: Sandra, estado antes do dinheiro (${iEstado} antes de ${iDinheiro})`)
      const dup = linhas.filter((l) => new Set(l.chips.map((c) => c.texto)).size !== l.chips.length).map((l) => l.nome)
      cobrar(dup.length === 0, `${w} px ${tema}: nenhum chip repetido na linha${dup.length ? ` (${dup.join(', ')})` : ''}`)
      await p.close()
    }
  }
  if (roda('B')) {
    for (const tema of TEMAS) for (const w of LARGURAS) {
      const p = await abrir(w, tema)
      await abrirAssistente(p)
      const m = await p.evaluate(() => {
        const cab = document.querySelector('[class*=colunaPainel] [class*=cabecalho]')
        const filhos = [...cab.children].filter((e) => e.getBoundingClientRect().width > 0)
        const rs = filhos.map((e) => e.getBoundingClientRect())
        const alto = Math.max(...rs.map((r) => r.height))
        const bloco = Math.max(...rs.map((r) => r.bottom)) - Math.min(...rs.map((r) => r.top))
        const s = cab.querySelector('h2 > span')
        const x = [...cab.querySelectorAll('button')].find((b) => /^Fechar/.test(b.getAttribute('aria-label') ?? ''))
        const xr = x?.getBoundingClientRect(); const cr = cab.getBoundingClientRect()
        return { uma: bloco <= alto + 2, bloco: Math.round(bloco), alto: Math.round(alto), titulo: s.scrollWidth <= s.clientWidth + 1, rotuloX: x?.getAttribute('aria-label'), xDentro: Boolean(xr && xr.right <= cr.right + 1 && xr.left >= cr.left) }
      })
      cobrar(m.uma && m.titulo && m.xDentro, `${w} px ${tema}: cabeçalho numa linha (bloco ${m.bloco} px, mais alto ${m.alto} px), título inteiro ${m.titulo}, X dentro ${m.xDentro}`)
      cobrar(m.rotuloX === 'Fechar (Esc)', `${w} px ${tema}: X com nome "Fechar (Esc)" (${m.rotuloX})`)
      await p.close()
    }
  }
  if (roda('C')) {
    for (const tema of TEMAS) for (const w of LARGURAS) {
      const p = await abrir(w, tema)
      await abrirAssistente(p)
      const nomes = (await p.evaluate(() => [...document.querySelectorAll('[class*=colunaPainel] [class*=cabecalho] button')].map((b) => b.getAttribute('aria-label')).filter(Boolean)))
        .filter((n) => /Encaixar|Flutuante|Largura/.test(n))
      cobrar(nomes.length >= 3, `${w} px ${tema}: ícones de posição com aria-label (${nomes.join(' | ')})`)
      for (const n of nomes) {
        await p.locator('[class*=colunaPainel] [class*=cabecalho]').getByRole('button', { name: n, exact: true }).first().hover()
        await p.waitForTimeout(800)
        const dica = await p.evaluate((nome) => [...document.querySelectorAll('[popover], [role=tooltip]')].some((e) => e.textContent.includes(nome) && e.getBoundingClientRect().width > 0), n)
        cobrar(dica, `${w} px ${tema}: dica visível ao apontar "${n}"`)
        await p.mouse.move(0, 0)
      }
      await p.close()
    }
  }
  if (roda('D')) {
    for (const tema of TEMAS) for (const w of LARGURAS) {
      const p = await abrir(w, tema)
      await abrirConversa(p, 'Marcelo')
      const medir = () => p.evaluate(() => {
        const t = [...document.querySelectorAll('[class*=modoTextoAviso]')].find((e) => e.getBoundingClientRect().width > 0)
        if (!t) return null
        const r = t.getBoundingClientRect()
        const folhas = [...t.querySelectorAll('span')]
        const dentro = (e) => { const q = e.getBoundingClientRect(); return q.width > 0 && q.top >= r.top - 1 && q.bottom <= r.bottom + 1 && q.right <= r.right + 1 }
        const vis = folhas.filter(dentro).map((e) => e.textContent.trim())
        const parcial = folhas.filter((e) => { const q = e.getBoundingClientRect(); return q.width > 0 && !dentro(e) && q.top < r.bottom - 1 }).map((e) => e.textContent.trim())
        const reticencia = !folhas.length && t.scrollWidth > t.clientWidth + 1
        return { vis, parcial, reticencia, titulo: t.title, icone: Boolean(t.querySelector('svg')), texto: t.textContent.trim() }
      })
      const sem = await medir()
      cobrar(Boolean(sem && !sem.reticencia && sem.parcial.length === 0 && /Sem lactose/.test(sem.texto)), `${w} px ${tema}: sem Assistente, alerta sem corte (${JSON.stringify(sem)})`)
      await p.getByRole('button', { name: /Abrir assistente/i }).first().click()
      await p.waitForTimeout(800)
      const com = await medir()
      cobrar(Boolean(com && com.vis.join(' ').includes('Sem lactose') && com.parcial.length === 0 && !com.reticencia && com.icone && /confira os ingredientes/.test(com.titulo ?? '')), `${w} px ${tema}: com Assistente, "Sem lactose" inteiro, sem corte e com ícone (${JSON.stringify(com)})`)
      await p.close()
    }
  }
} finally {
  await navegador.close()
  await servidor.close()
}
if (falhas.length) { console.log(`prova-3386-r8: ${falhas.length} falha(s)`); process.exit(1) }
console.log('prova-3386-r8: ok')
