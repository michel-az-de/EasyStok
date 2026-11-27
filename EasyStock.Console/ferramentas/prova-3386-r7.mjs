/* eslint-disable no-console */
// #3386 (crítica r7, 28/40 para 29): provas renderizadas do Atendimento em 1024, 1280 e 1440 px, claro e escuro.
// A: nenhum chip da fila aparece cortado. B: o título do Assistente cabe. C: o trilho mostra todos os itens sem rolagem escondida.
// D: ao assumir, "Enviar cardápio" e "Assistente" não mudam de lugar.
// Uso: node ferramentas/prova-3386-r7.mjs [A|B|C|D ...]  (sem argumento roda tudo)
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const itens = new Set(process.argv.slice(2).map((s) => s.toUpperCase()))
const roda = (i) => itens.size === 0 || itens.has(i)
const raiz = fileURLToPath(new URL('..', import.meta.url))
const servidor = await createServer({ root: raiz, logLevel: 'error', server: { host: '127.0.0.1', port: 5822, strictPort: false } })
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

// Cada chip da fila: se aparece na primeira fileira, tem de aparecer inteiro.
const medirChips = (p) => p.evaluate(() => [...document.querySelectorAll('button[data-conversa-id]')].map((b) => {
  const linha = b.querySelector('[class*=contextoLinha]')
  if (!linha) return null
  const lr = linha.getBoundingClientRect()
  const chips = [...linha.children].map((c) => {
    const r = c.getBoundingClientRect()
    const naFileira = r.width > 0 && r.top >= lr.top - 1 && r.bottom <= lr.bottom + 1 && r.left < lr.right - 1
    const t = c.querySelector('[class*=texto]') ?? c
    return { texto: c.textContent.trim(), naFileira, cortado: naFileira && (r.right > lr.right + 1 || c.scrollWidth > c.clientWidth + 1 || t.scrollWidth > t.clientWidth + 1) }
  })
  return { nome: b.querySelector('strong')?.textContent.trim().slice(0, 12), chips }
}).filter(Boolean))

try {
  if (roda('A')) {
    for (const tema of TEMAS) for (const w of LARGURAS) {
      const p = await abrir(w, tema)
      const linhas = await medirChips(p)
      const cortados = linhas.flatMap((l) => l.chips.filter((c) => c.cortado).map((c) => `${l.nome}: ${c.texto}`))
      const semNenhum = linhas.filter((l) => l.chips.length && !l.chips.some((c) => c.naFileira && !c.cortado)).map((l) => l.nome)
      cobrar(linhas.length > 5 && cortados.length === 0, `${w} px ${tema}: nenhum chip da fila cortado${cortados.length ? ` (${cortados.join('; ')})` : ''}`)
      cobrar(semNenhum.length === 0, `${w} px ${tema}: toda linha com chips mostra ao menos um inteiro${semNenhum.length ? ` (${semNenhum.join(', ')})` : ''}`)
      await p.close()
    }
  }
  if (roda('B')) {
    for (const tema of TEMAS) for (const w of LARGURAS) {
      const p = await abrir(w, tema)
      await abrirConversa(p, 'Marcelo')
      await p.getByRole('button', { name: /Abrir assistente/i }).first().click()
      await p.waitForTimeout(700)
      const m = await p.evaluate(() => {
        const s = document.querySelector('[class*=colunaPainel] [class*=cabecalho] h2 > span')
        if (!s) return null
        return { texto: s.textContent.trim(), cortado: s.scrollWidth > s.clientWidth + 1, largura: Math.round(s.clientWidth) }
      })
      cobrar(m && !m.cortado && m.largura > 60, `${w} px ${tema}: título do Assistente inteiro (${m ? `${m.largura} px, cortado ${m.cortado}` : 'não achado'})`)
      await p.close()
    }
  }
  if (roda('C')) {
    const contagem = {}
    for (const tema of TEMAS) for (const w of [1280, 1440]) {
      const p = await abrir(w, tema)
      const m = await p.evaluate(() => {
        const nav = [...document.querySelectorAll('nav')].find((n) => /Atendimento/.test(n.innerText) && /Ajuda/.test(n.innerText))
        const lista = [...nav.querySelectorAll('*')].find((e) => ['auto', 'scroll'].includes(getComputedStyle(e).overflowY) && /Atendimento/.test(e.innerText))
        const lr = lista.getBoundingClientRect()
        const filhos = [...lista.children].filter((e) => e.getBoundingClientRect().width > 0)
        const fora = filhos.filter((e) => { const r = e.getBoundingClientRect(); return r.bottom > lr.bottom + 1 || r.top < lr.top - 1 }).map((e) => e.innerText.trim() || e.getAttribute('aria-label') || e.title)
        return { rolagem: lista.scrollHeight - lista.clientHeight, n: filhos.length, fora, texto: lista.innerText }
      })
      contagem[`${w}${tema}`] = m.n
      cobrar(m.rolagem <= 1 && m.fora.length === 0, `${w} px ${tema}: trilho sem rolagem escondida (sobra ${m.rolagem} px; fora: ${m.fora.join(', ') || 'nenhum'})`)
      cobrar(/Disposição/.test(m.texto) && /Ajuda/.test(m.texto), `${w} px ${tema}: Disposição e Ajuda no trilho`)
      await p.close()
    }
    const valores = new Set(Object.values(contagem))
    cobrar(valores.size === 1, `1280 e 1440 com o mesmo número de itens no trilho (${JSON.stringify(contagem)})`)
  }
  if (roda('D')) {
    for (const tema of TEMAS) for (const w of [1280, 1440]) {
      const p = await abrir(w, tema)
      await abrirConversa(p, 'Marcelo')
      const pos = () => p.evaluate(() => {
        const x = (re) => { const b = [...document.querySelectorAll('header button')].find((e) => re.test(e.innerText + (e.getAttribute('aria-label') ?? ''))); return b ? Math.round(b.getBoundingClientRect().left) : null }
        return { cardapio: x(/Enviar cardápio/), assistente: x(/Assistente/) }
      })
      const antes = await pos()
      await p.getByRole('button', { name: /^Assumir conversa/ }).click()
      await p.waitForTimeout(800)
      const depois = await pos()
      cobrar(antes.cardapio !== null && antes.cardapio === depois.cardapio && antes.assistente === depois.assistente,
        `${w} px ${tema}: botões do cabeçalho no mesmo lugar ao assumir (cardápio ${antes.cardapio} para ${depois.cardapio}, Assistente ${antes.assistente} para ${depois.assistente})`)
      await p.close()
    }
  }
} finally {
  await navegador.close()
  await servidor.close()
}
if (falhas.length) { console.log(`prova-3386-r7: ${falhas.length} falha(s)`); process.exit(1) }
console.log('prova-3386-r7: ok')
