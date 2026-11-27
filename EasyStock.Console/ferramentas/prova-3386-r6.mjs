/* eslint-disable no-console */
// #3386 (crítica r6, 27/40 para 29): provas renderizadas da tela de Atendimento em 1024, 1280 e 1440 px.
// A: consistência da fila entre larguras. B: rodapé fixo da Ficha. C: Ajuda no trilho. D: "Enviar e assumir" desabilitado.
// Uso: node ferramentas/prova-3386-r6.mjs [A|B|C|D ...]  (sem argumento roda tudo)
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const itens = new Set(process.argv.slice(2).map((s) => s.toUpperCase()))
const roda = (i) => itens.size === 0 || itens.has(i)
const raiz = fileURLToPath(new URL('..', import.meta.url))
const servidor = await createServer({ root: raiz, logLevel: 'error', server: { host: '127.0.0.1', port: 5821, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const falhas = []
const cobrar = (ok, msg) => { console.log(ok ? 'ok   ' : 'FALHA', msg); if (!ok) falhas.push(msg) }
const LARGURAS = [1024, 1280, 1440]

async function abrir(w, h = 900) {
  const p = await navegador.newPage({ viewport: { width: w, height: h } })
  await p.goto(`${origem}/#/m/atendimento/balcao`, { timeout: 90000 })
  await p.locator('button[data-conversa-id]').first().waitFor({ timeout: 30000 })
  await p.waitForTimeout(700)
  return p
}
async function abrirConversa(p, nome) {
  await p.locator('button[data-conversa-id]', { hasText: nome }).first().click()
  await p.waitForTimeout(700)
}

// Mede cada linha da fila: nome visível, "Pago", relógio de prazo.
const medirFila = (p) => p.evaluate(() => [...document.querySelectorAll('button[data-conversa-id]')].slice(0, 12).map((b) => {
  const linha = b.querySelector('[class*=contextoLinha]')
  const lr = linha?.getBoundingClientRect()
  const nomeEl = b.querySelector('strong [class*=nome]')
  const cand = nomeEl ? [...nomeEl.querySelectorAll('[class*=completo],[class*=curto]')] : []
  const visivel = cand.find((e) => getComputedStyle(e).display !== 'none' && getComputedStyle(e).clipPath === 'none')
  const pago = [...(linha?.children ?? [])].find((e) => e.textContent.trim() === 'Pago')
  const pr = pago?.getBoundingClientRect()
  const rel = b.querySelector('[aria-label="Prazo de resposta estourado"]')
  const label = b.getAttribute('aria-label') ?? ''
  return {
    nome: (cand.find((e) => /completo/.test(e.className))?.textContent ?? '').trim(),
    texto: visivel?.textContent.trim() ?? null,
    truncado: visivel ? visivel.scrollWidth > visivel.clientWidth + 1 : false,
    devePago: /, Pago,|, Pago$/.test(label),
    pagoVisivel: Boolean(pago && pr.width > 0 && pr.top < lr.bottom - 4 && pr.right <= lr.right + 1),
    pagoCortado: pago ? pago.scrollWidth > pago.clientWidth + 1 : false,
    deveRelogio: /prazo de resposta estourado/.test(label),
    relogio: Boolean(rel && rel.getBoundingClientRect().width > 0),
  }
}))

const SEPARADOR = /\d{1,2}h\d{0,2}\s*(às|–|-)\s*\d{1,2}h\d{0,2}/g
async function textoDaFicha(p, w) {
  if (w < 1180) {
    await p.getByRole('button', { name: /^Ficha de / }).click()
    await p.getByRole('button', { name: 'Fechar Ficha' }).waitFor({ timeout: 5000 })
    await p.waitForTimeout(300)
    const t = await p.evaluate(() => document.querySelector('dialog[aria-label="Ficha"]').innerText)
    await p.getByRole('button', { name: 'Fechar Ficha' }).click()
    await p.waitForTimeout(300)
    return t
  }
  return p.evaluate(() => document.querySelector('[data-cabecalho-ficha]').parentElement.innerText)
}

// Estrutura da ficha: conteúdo rolável, rodapé fixo, indicador.
const medirFicha = (p) => p.evaluate(async () => {
  const painel = document.querySelector('[data-cabecalho-ficha]').parentElement
  const rolavel = [...painel.children].find((e) => getComputedStyle(e).overflowY === 'auto')
  const rodape = [...painel.children].find((e) => /barraPasso/.test(e.className))
  const medida = () => {
    const rr = rolavel.getBoundingClientRect()
    const ultimo = rolavel.lastElementChild?.getBoundingClientRect()
    const fr = rodape?.getBoundingClientRect()
    return {
      rolavel: rolavel.scrollHeight > rolavel.clientHeight + 2,
      sobreposto: fr ? Math.max(0, Math.round(rr.bottom - fr.top)) : 0,
      escondido: fr && ultimo ? Math.max(0, Math.round(ultimo.bottom - fr.top)) : 0,
      folga: parseFloat(getComputedStyle(rolavel).paddingBottom) || 0,
      altRodape: fr ? Math.round(fr.height) : 0,
      indicador: rolavel.dataset.maisAbaixo === 'true',
      indicadorVisivel: (getComputedStyle(rolavel).maskImage || getComputedStyle(rolavel).webkitMaskImage || 'none') !== 'none',
    }
  }
  const antes = medida()
  rolavel.scrollTop = rolavel.scrollHeight
  await new Promise((r) => setTimeout(r, 250))
  const fim = medida()
  rolavel.scrollTop = 0
  return { antes, fim }
})

try {
  if (roda('A')) {
    const nomes = new Map()
    const separadores = new Set()
    let amostrasFaixa = 0
    for (const w of LARGURAS) {
      const p = await abrir(w)
      for (const estado of ['lista', 'aberta']) {
        if (estado === 'aberta') await abrirConversa(p, 'Marcelo')
        const rotulo = `${w} px, ${estado}`
        const linhas = await medirFila(p)
        for (const l of linhas) {
          if (l.texto) {
            if (!nomes.has(l.nome)) nomes.set(l.nome, new Set())
            nomes.get(l.nome).add(l.texto)
          }
        }
        const trunc = linhas.filter((l) => l.truncado).map((l) => l.texto)
        cobrar(trunc.length === 0, `${rotulo}: nenhum nome cortado com reticências${trunc.length ? ` (${trunc.join(', ')})` : ''}`)
        // #3386 (r7): chip cortado é pior que chip ausente; quando o "Pago" não cabe inteiro ele sai da fileira e fica no aria-label e no title.
  const semPago = linhas.filter((l) => l.devePago && l.pagoVisivel && l.pagoCortado).map((l) => l.nome)
        cobrar(linhas.some((l) => l.devePago) && semPago.length === 0, `${rotulo}: chip "Pago" nunca cortado em linha paga${semPago.length ? ` (cortado em ${semPago.join(', ')})` : ''}`)
        const semRel = linhas.filter((l) => l.deveRelogio && !l.relogio).map((l) => l.nome)
        cobrar(linhas.some((l) => l.deveRelogio) && semRel.length === 0, `${rotulo}: relógio em toda linha com prazo estourado${semRel.length ? ` (faltou em ${semRel.join(', ')})` : ''}`)
      }
      // Ponto de status do cabeçalho da conversa.
      for (const nome of ['Sandra', 'Bianca', 'Marcelo']) {
        await abrirConversa(p, nome)
        const ponto = await p.evaluate(() => {
          const e = document.querySelector('[data-controle-conversa] [class*=modoEstado] > span')
          if (!e) return null
          const b = getComputedStyle(e, '::before')
          return b.content !== 'none' && parseFloat(b.width) > 0
        })
        cobrar(ponto === true, `${w} px: ${nome} tem o ponto de status no cabeçalho`)
        const t = await textoDaFicha(p, w)
        for (const m of t.matchAll(SEPARADOR)) { separadores.add(m[1]); amostrasFaixa += 1 }
      }
      await p.close()
    }
    const instaveis = [...nomes].filter(([, v]) => v.size > 1).map(([k, v]) => `${k}: ${[...v].join(' / ')}`)
    cobrar(nomes.size > 0 && instaveis.length === 0, `o nome na fila tem uma forma só em todas as larguras${instaveis.length ? ` (${instaveis.join('; ')})` : ''}`)
    cobrar(amostrasFaixa >= 2 && separadores.size === 1, `a faixa de horário tem um formato só (${[...separadores].join(' e ') || 'nenhuma achada'}; ${amostrasFaixa} amostras)`)
  }

  if (roda('B')) {
    for (const w of LARGURAS) {
      const p = await abrir(w, 800)
      for (const nome of ['Sandra', 'Bianca']) {
        await abrirConversa(p, nome)
        if (w < 1180) { await p.getByRole('button', { name: /^Ficha de / }).click(); await p.waitForTimeout(500) }
        const { antes, fim } = await medirFicha(p)
        const r = `${w} px, ${nome}`
        cobrar(fim.sobreposto === 0 && fim.escondido === 0, `${r}: rolado até o fim, nada fica sob o rodapé (sobreposto ${fim.sobreposto}, escondido ${fim.escondido})`)
        if (antes.rolavel) {
          cobrar(antes.indicador && antes.indicadorVisivel, `${r}: há mais abaixo e o indicador de rolagem aparece`)
          cobrar(!fim.indicador, `${r}: no fim da rolagem o indicador some`)
        } else {
          cobrar(!antes.indicador, `${r}: sem o que rolar, sem indicador`)
        }
      }
      await p.close()
    }
  }

  if (roda('C')) {
    const dialogos = []
    for (const w of LARGURAS) {
      const p = await abrir(w)
      const botao = p.getByRole('button', { name: 'Ajuda', exact: true }).first()
      const existe = await botao.isVisible().catch(() => false)
      cobrar(existe, `${w} px: "Ajuda" à vista na navegação`)
      if (existe) {
        await botao.click()
        const nome = await p.getByRole('dialog').first().waitFor({ timeout: 5000 }).then(() => p.evaluate(() => {
          const d = document.querySelector('dialog[open], [role=dialog]')
          return d?.getAttribute('aria-label') ?? d?.querySelector('h1,h2')?.textContent ?? ''
        }), () => null)
        dialogos.push(nome)
        cobrar(Boolean(nome), `${w} px: "Ajuda" abre a ajuda (${nome})`)
      }
      await p.close()
    }
    cobrar(new Set(dialogos).size === 1, 'o destino da Ajuda é o mesmo nas três larguras')
  }

  if (roda('D')) {
    for (const w of LARGURAS) {
      const p = await abrir(w)
      await abrirConversa(p, 'Marcelo')
      const b = p.getByRole('button', { name: 'Enviar e assumir a conversa' })
      const visivel = await b.isVisible().catch(() => false)
      cobrar(visivel, `${w} px: o botão "Enviar e assumir" aparece`)
      if (visivel) {
        const m = await b.evaluate((el) => {
          const c = getComputedStyle(el)
          return { desabilitado: el.disabled, estilo: c.borderTopStyle, cursor: c.cursor }
        })
        cobrar(m.desabilitado && m.estilo === 'dashed' && m.cursor === 'not-allowed', `${w} px: desabilitado com o desenho do sistema (contorno ${m.estilo}, cursor ${m.cursor})`)
      }
      await p.close()
    }
  }
} finally {
  await navegador.close()
  await servidor.close()
}
if (falhas.length) { console.log(`prova-3386-r6: ${falhas.length} falha(s)`); process.exit(1) }
console.log('prova-3386-r6: ok')
