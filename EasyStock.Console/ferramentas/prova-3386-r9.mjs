/* eslint-disable no-console */
// #3386 (crítica r9): provas renderizadas do Atendimento em 1024, 1280 e 1440 px, claro e escuro.
// A: no tablet o cartão de duas fileiras mede até 100 px e a fila mostra 4 conversas inteiras a 1024 x 768.
// B: o cabeçalho do Assistente mostra o nome inteiro (ou a forma curta da fila), sem "Esc" solto, e o X leva "Fechar (Esc)".
// C: "Enviar e assumir" desabilitado sem contorno (#3392), cursor not-allowed.
// D: o chip "Precisa de você" aparece inteiro em toda largura, com o Assistente aberto ou fechado.
// E: a ordem do Tab no campo de mensagem segue a ordem visual, sem tabindex positivo.
// F (revisão do PR): o alerta de restrição tem sempre uma parte essencial curta e inteira, e o title nunca é vazio (390, 1024, 1280).
// G: a 1280 x 800 o cartão de alerta (tom negativo, foto, dica do CDC) nunca fica recolhido sem dizer o estado em texto.
// H: reserva de altura da linha de contexto (zero sem linha), filtro de chips repetidos só com texto.
// Uso: node ferramentas/prova-3386-r9.mjs [A|B|C|D|E|F|G|H ...]  (sem argumento roda tudo)
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const itens = new Set(process.argv.slice(2).map((s) => s.toUpperCase()))
const roda = (i) => itens.size === 0 || itens.has(i)
const raiz = fileURLToPath(new URL('..', import.meta.url))
const servidor = await createServer({ root: raiz, logLevel: 'error', ssr: { external: ['react', 'react-dom'] }, server: { host: '127.0.0.1', port: 5824, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const falhas = []
const cobrar = (ok, msg) => { console.log(ok ? 'ok···' : 'FALHA', msg); if (!ok) falhas.push(msg) }
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

try {
  if (roda('A')) for (const tema of TEMAS) {
    const p = await abrir(1024, tema)
    const m = await p.evaluate(() => {
      const cs = [...document.querySelectorAll('button[data-conversa-id]')]
      let rolagem = cs[0].parentElement
      while (rolagem && !/(auto|scroll)/.test(getComputedStyle(rolagem).overflowY)) rolagem = rolagem.parentElement
      const rr = rolagem.getBoundingClientRect()
      const inteiras = cs.filter((c) => { const r = c.getBoundingClientRect(); return r.top >= rr.top - 1 && r.bottom <= rr.bottom + 1 }).length
      const duas = cs.filter((c) => { const l = c.querySelector('[class*=contextoLinha]'); return l && new Set([...l.children].map((e) => Math.round(e.getBoundingClientRect().top))).size >= 2 })
        .map((c) => Math.round(c.getBoundingClientRect().height))
      return { inteiras, duas }
    })
    cobrar(m.duas.length > 0 && Math.max(...m.duas) <= 100, `1024 px ${tema}: cartão de 2 fileiras com no máximo 100 px (${m.duas.join(',')})`)
    cobrar(m.inteiras >= 4, `1024 px ${tema}: ao menos 4 conversas inteiras na fila (${m.inteiras})`)
    await p.close()
  }

  if (roda('B')) for (const w of LARGURAS) for (const tema of TEMAS) {
    const p = await abrir(w, tema)
    await abrirAssistente(p)
    const m = await p.evaluate(() => {
      const nome = document.querySelector('[class*=clienteDoPainel]')
      const cab = nome?.closest('[class*=cabecalho]')
      if (!cab) return null
      const x = [...cab.querySelectorAll('button')].find((b) => /^Fechar/.test(b.getAttribute('aria-label') ?? ''))
      return {
        nome: nome.textContent.trim(), cortado: nome.scrollWidth > nome.clientWidth + 1,
        esc: [...cab.querySelectorAll('*')].some((e) => e.children.length === 0 && /^Esc$/.test(e.textContent.trim()) && e.getBoundingClientRect().width > 0),
        rotuloX: x?.getAttribute('aria-label'),
      }
    })
    cobrar(Boolean(m) && m.nome && !m.cortado && /^Marcelo T(eixeira|\.)/.test(m.nome), `${w} px ${tema}: nome do cliente inteiro ou curto, sem corte (${JSON.stringify(m)})`)
    cobrar(Boolean(m) && !m.esc && m.rotuloX === 'Fechar (Esc)', `${w} px ${tema}: nunca o texto "Esc"; X com "Fechar (Esc)"`)
    await p.close()
  }

  if (roda('C')) for (const w of LARGURAS) for (const tema of TEMAS) {
    const p = await abrir(w, tema)
    let achado = null
    const total = await p.locator('button[data-conversa-id]').count()
    for (let i = 0; i < Math.min(total, 14) && !achado; i++) {
      await p.locator('button[data-conversa-id]').nth(i).click()
      await p.waitForTimeout(400)
      achado = await p.evaluate(() => {
        const b = [...document.querySelectorAll('button')].find((e) => e.getAttribute('aria-label') === 'Enviar e assumir a conversa' && e.disabled)
        if (!b) return null
        const s = getComputedStyle(b)
        const sonda = document.createElement('i'); sonda.style.borderColor = 'var(--linha)'; sonda.style.borderStyle = 'solid'; document.body.append(sonda)
        const linha = getComputedStyle(sonda).borderTopColor; sonda.remove()
        return { estilo: s.borderTopStyle, largura: s.borderTopWidth, cor: s.borderTopColor, linha, cursor: s.cursor }
      })
    }
    // #3392 (r10, H8): o contorno virou destaque; o desabilitado agora não tem contorno, como os outros da barra.
    cobrar(Boolean(achado) && (achado.estilo === 'none' || achado.cor === 'rgba(0, 0, 0, 0)') && achado.cursor === 'not-allowed',
      `${w} px ${tema}: "Enviar e assumir" desabilitado sem contorno, not-allowed (${JSON.stringify(achado)})`)
    await p.close()
  }

  if (roda('D')) for (const w of LARGURAS) for (const tema of TEMAS) for (const comAssistente of [false, true]) {
    const p = await abrir(w, tema)
    if (comAssistente) await abrirAssistente(p); else await abrirConversa(p, 'Marcelo')
    const m = await p.evaluate(() => {
      const b = [...document.querySelectorAll('main button')].find((e) => /^Precisa de você/.test(e.textContent.trim()))
      if (!b) return null
      const r = b.getBoundingClientRect()
      let c = b.parentElement
      while (c && getComputedStyle(c).overflow === 'visible') c = c.parentElement
      const rc = c.getBoundingClientRect()
      return { dir: Math.round(r.right), limite: Math.round(rc.right), esq: Math.round(r.left), limiteEsq: Math.round(rc.left), texto: b.textContent.trim() }
    })
    cobrar(Boolean(m) && m.dir <= m.limite + 1 && m.esq >= m.limiteEsq - 1, `${w} px ${tema} ${comAssistente ? 'com' : 'sem'} Assistente: chip "Precisa de você" inteiro (${JSON.stringify(m)})`)
    await p.close()
  }

  if (roda('E')) for (const w of LARGURAS) for (const tema of TEMAS) {
    const p = await abrir(w, tema)
    await abrirConversa(p, 'Bianca')
    await p.locator('textarea').first().focus()
    const seq = []
    for (let i = 0; i < 6; i++) {
      await p.keyboard.press('Tab')
      seq.push(await p.evaluate(() => {
        const e = document.activeElement
        return { r: (e.getAttribute('aria-label') || e.textContent || '').trim().slice(0, 20), x: Math.round(e.getBoundingClientRect().left), dentro: Boolean(e.closest('[class*=acoes]')) }
      }))
    }
    const barra = seq.filter((s) => s.dentro)
    const crescente = barra.length >= 4 && barra.every((s, i) => i === 0 || s.x >= barra[i - 1].x)
    const positivo = await p.evaluate(() => [...document.querySelectorAll('[tabindex]')].some((e) => Number(e.getAttribute('tabindex')) > 0))
    cobrar(crescente && !positivo, `${w} px ${tema}: Tab segue a ordem visual (${barra.map((s) => `${s.r}@${s.x}`).join(' > ')})`)
    await p.close()
  }

  if (roda('F')) {
    const { partesDoAvisoDeRestricao } = await servidor.ssrLoadModule('/src/dominio/resumoDoModo.js')
    const { avisoDeRestricao } = await servidor.ssrLoadModule('/src/dominio/automatico.js')
    const genericoTexto = avisoDeRestricao(null).texto
    const g = typeof partesDoAvisoDeRestricao === 'function' ? partesDoAvisoDeRestricao(genericoTexto) : null
    cobrar(g?.essencial === 'Restrição alimentar' && `${g.essencial}${g.resto}` === genericoTexto, `restrição sem item: essencial curto e inteiro, o resto no detalhe (${JSON.stringify(g)})`)
    const c = typeof partesDoAvisoDeRestricao === 'function' ? partesDoAvisoDeRestricao('Sem lactose: confira os ingredientes') : null
    cobrar(c?.essencial === 'Sem lactose' && c.resto === ': confira os ingredientes', `restrição com item: "Sem lactose" é o essencial (${JSON.stringify(c)})`)
    for (const [w, h] of [[390, 844], [1024, 768], [1280, 800]]) {
      const p = await navegador.newPage({ viewport: { width: w, height: h } })
      await p.goto(`${origem}/#/m/atendimento/balcao`, { timeout: 90000 })
      await p.locator('button[data-conversa-id]').first().waitFor({ timeout: 30000 })
      await p.waitForTimeout(700)
      let achado = null
      const total = await p.locator('button[data-conversa-id]').count()
      for (let i = 0; i < Math.min(total, 20) && !achado; i++) {
        await p.locator('button[data-conversa-id]').nth(i).click()
        await p.waitForTimeout(350)
        achado = await p.evaluate(() => {
          const t = [...document.querySelectorAll('[class*=modoTextoAviso]')].find((e) => e.getBoundingClientRect().width > 0)
          const e = t?.querySelector('[class*=modoEssencial]')
          return e ? { cortado: e.scrollWidth > e.clientWidth || e.getBoundingClientRect().right > t.getBoundingClientRect().right + 1, texto: e.textContent.trim(), title: t.getAttribute('title') ?? '' } : null
        })
      }
      cobrar(Boolean(achado) && !achado.cortado && achado.title.length > 0, `${w} px: essencial da restrição sem corte e com title (${JSON.stringify(achado)})`)
      await p.close()
    }
  }

  if (roda('G')) {
    // Tela baixa (1280 x 800): o cartão chamado com `matchMedia` de altura < 900 nasce recolhido; o de alerta não pode.
    globalThis.window = { matchMedia: () => ({ matches: true }) }
    const req = createRequire(import.meta.url)
    const { createElement: h } = req('react')
    const { renderToString } = req('react-dom/server')
    const C = await servidor.ssrLoadModule('/src/features/assistente/Cartoes.jsx')
    const visivel = (html) => html.replace(/<div[^>]*hidden=""[^>]*>[\s\S]*?<\/div>/g, '').replace(/<[^>]+>/g, ' ')
    const casos = [
      ['Tom negativo', h(C.CartaoSentimento, { dado: { classificacao: 'negativo', trecho: 'péssimo' }, aoDispensar() {} }), /Negativo/],
      ['Dica do CDC', h(C.CartaoCDC, { dado: { titulo: 'Direito de arrependimento', artigo: 'Art. 49', dica: 'Sete dias.' }, aoDispensar() {} }), /Art\. 49/],
      ['Foto não analisada', h(C.CartaoFotoSuspeita, { dado: { confianca: 0.8, motivos: ['a'] }, aoDispensar() {} }), /não foi analisada/],
    ]
    for (const [nome, el, esperado] of casos) {
      const html = renderToString(el)
      cobrar(esperado.test(visivel(html)), `tela baixa: "${nome}" diz o estado em texto à vista, mesmo recolhido`)
    }
    delete globalThis.window
  }

  if (roda('H')) {
    const { medidaDaLinha, pilulasDaLinha } = await servidor.ssrLoadModule('/src/features/caixa-de-entrada/apresentacaoDoCartao.js')
    const f = typeof medidaDaLinha === 'function'
    cobrar(f && medidaDaLinha(null) === 0 && medidaDaLinha({}) === 0 && medidaDaLinha({ getBoundingClientRect: () => ({ height: 22 }) }) === 22,
      'reserva da linha de contexto: zero sem linha ou sem DOM real, a altura medida quando há')
    const vazios = pilulasDaLinha(null, false, [{ chave: 'a' }, { chave: 'b' }, { texto: 'Pago' }, { texto: ' pago ' }])
    cobrar(vazios.length === 3, `chips repetidos: só descarta o que tem texto (restaram ${vazios.length} de 4, esperado 3)`)
  }
} finally {
  await navegador.close()
  await servidor.close()
}
if (falhas.length) { console.log(`prova-3386-r9: ${falhas.length} falha(s)`); process.exit(1) }
console.log('prova-3386-r9: ok')
