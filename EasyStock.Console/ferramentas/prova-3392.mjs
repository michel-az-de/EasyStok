/* eslint-disable no-console */
// #3392 (críticas r8 a r10): itens estruturais do Atendimento, renderizados em 1024, 1280 e 1440 px.
// A: o Assistente encaixado abre do mesmo lado (esquerdo) em toda largura.
// B: atalhos J (próxima conversa), K (anterior) e "/" (busca da fila); nenhum dispara com foco em campo de texto; a Ajuda lista os três.
// C: a 1024 x 768, com a conversa assumida, a área das mensagens ocupa pelo menos 55% da altura.
// D: "Enviar" desabilitado não tem contorno: igual aos outros desabilitados da barra.
// Uso: node ferramentas/prova-3392.mjs [A|B|C|D ...]  (sem argumento roda tudo)
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const itens = new Set(process.argv.slice(2).map((s) => s.toUpperCase()))
const roda = (i) => itens.size === 0 || itens.has(i)
const raiz = fileURLToPath(new URL('..', import.meta.url))
const servidor = await createServer({ root: raiz, logLevel: 'error', ssr: { external: ['react', 'react-dom'] }, server: { host: '127.0.0.1', port: 5826, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const falhas = []
const cobrar = (ok, msg) => { console.log(ok ? 'ok···' : 'FALHA', msg); if (!ok) falhas.push(msg) }
const LARGURAS = [1024, 1280, 1440]
const ALTURA = { 1024: 768, 1280: 800, 1440: 900 }

async function abrir(w, tema = 'light') {
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
const atual = (p) => p.evaluate(() => document.querySelector('button[data-conversa-id][aria-current="true"]')?.dataset.conversaId ?? null)
const ordem = (p) => p.evaluate(() => [...document.querySelectorAll('button[data-conversa-id]')].map((b) => b.dataset.conversaId))
// Tira o foco de qualquer campo: o atalho só vale fora de campo de texto.
const soltarFoco = (p) => p.evaluate(() => { document.activeElement?.blur?.(); document.body.focus() })

try {
  if (roda('A')) {
    const lados = {}
    for (const w of LARGURAS) {
      const p = await abrir(w)
      await abrirConversa(p, 'Marcelo')
      await p.getByRole('button', { name: /Abrir assistente/i }).first().click()
      await p.waitForTimeout(900)
      const m = await p.evaluate(() => {
        const s = [...document.querySelectorAll('section[role=dialog]')].find((e) => e.getBoundingClientRect().width > 0)
        if (!s) return null
        const r = s.getBoundingClientRect()
        const vis = (rotulo) => { const e = document.querySelector(`section[aria-label='${rotulo}']`); return Boolean(e && e.offsetParent !== null && e.getBoundingClientRect().width > 0) }
        const conversa = document.querySelector("section[aria-label='Conversa']")?.getBoundingClientRect()
        return {
          centro: Math.round(r.left + r.width / 2), largura: innerWidth, fila: vis('Conversas'), ficha: vis('Ficha'),
          cobre: conversa ? Math.max(0, Math.round(r.right - conversa.left)) : null, conversa: conversa ? Math.round(conversa.width) : null,
        }
      })
      lados[w] = m ? (m.centro < m.largura / 2 ? 'esquerda' : 'direita') : null
      // A fila cede o lugar ao painel em toda largura; a conversa nunca fica por baixo dele; a Ficha fica a 1280 e 1440.
      cobrar(Boolean(m) && !m.fila && m.cobre === 0 && (w === 1024 || m.ficha), `${w} px: Assistente no lugar da fila, sem cobrir a conversa${w === 1024 ? '' : ', Ficha à vista'} (${JSON.stringify(m)})`)
      await p.close()
    }
    cobrar(LARGURAS.every((w) => lados[w] === 'esquerda'), `Assistente do mesmo lado (esquerdo) em 1024, 1280 e 1440 (${JSON.stringify(lados)})`)
    // Quem escolhe a direita no computador continua à direita depois de recarregar.
    const p = await abrir(1280)
    await abrirConversa(p, 'Marcelo')
    await p.getByRole('button', { name: /Abrir assistente/i }).first().click()
    await p.waitForTimeout(700)
    await p.getByRole('button', { name: 'Encaixar à direita' }).click()
    await p.reload()
    await p.locator('button[data-conversa-id]').first().waitFor({ timeout: 30000 })
    await abrirConversa(p, 'Marcelo')
    const aberto = await p.locator('section[role=dialog]').filter({ visible: true }).count()
    if (!aberto) { await p.getByRole('button', { name: /Abrir assistente/i }).first().click() }
    await p.waitForTimeout(900)
    const lado = await p.evaluate(() => {
      const s = [...document.querySelectorAll('section[role=dialog]')].find((e) => e.getBoundingClientRect().width > 0)
      return s ? (s.getBoundingClientRect().left + s.getBoundingClientRect().width / 2 > innerWidth / 2 ? 'direita' : 'esquerda') : null
    })
    cobrar(lado === 'direita', `1280 px: a escolha "Encaixar à direita" sobrevive ao recarregar (${lado})`)
    await p.close()
  }

  if (roda('B')) for (const w of LARGURAS) {
    const p = await abrir(w)
    const lista = await ordem(p)
    await abrirConversa(p, 'Marcelo')
    const antes = await atual(p)
    const i = lista.indexOf(antes)
    await soltarFoco(p)
    await p.keyboard.press('j')
    await p.waitForTimeout(500)
    const depoisJ = await atual(p)
    await soltarFoco(p)
    await p.keyboard.press('k')
    await p.waitForTimeout(500)
    const depoisK = await atual(p)
    cobrar(i >= 0 && depoisJ === lista[i + 1] && depoisK === antes, `${w} px: J vai para a próxima e K volta (${JSON.stringify({ antes, depoisJ, depoisK, proxima: lista[i + 1] })})`)
    await soltarFoco(p)
    await p.keyboard.press('/')
    await p.waitForTimeout(300)
    const busca = await p.evaluate(() => ({ id: document.activeElement?.id, valor: document.activeElement?.value ?? null }))
    cobrar(busca.id === 'balcao-busca' && busca.valor === '', `${w} px: "/" leva o foco à busca da fila, sem digitar a barra (${JSON.stringify(busca)})`)
    // Dentro do campo de mensagem, J é letra e não troca de conversa.
    await p.keyboard.press('Escape')
    const campo = p.locator('textarea').first()
    let letraOk = true
    if (await campo.count()) {
      const antesCampo = await atual(p)
      await campo.focus()
      await p.keyboard.press('j')
      await p.waitForTimeout(300)
      letraOk = (await atual(p)) === antesCampo && (await campo.inputValue()).endsWith('j')
    }
    cobrar(letraOk, `${w} px: com foco no campo de mensagem, J digita a letra e não troca de conversa`)
    await p.close()
  }
  if (roda('B')) {
    const p = await abrir(1280)
    await p.getByRole('button', { name: /^Ajuda$/ }).first().click()
    await p.waitForTimeout(700)
    const t = await p.evaluate(() => document.body.innerText)
    cobrar(/Próxima conversa/i.test(t) && /Conversa anterior/i.test(t) && /Buscar conversa/i.test(t), 'a Ajuda lista J (próxima), K (anterior) e "/" (buscar conversa)')
    const declarados = () => p.evaluate(() => ({
      lista: document.querySelector('[role=list][aria-label=Conversas]')?.getAttribute('aria-keyshortcuts') ?? null,
      busca: document.getElementById('balcao-busca')?.getAttribute('aria-keyshortcuts') ?? null,
    }))
    const ligados = await declarados()
    cobrar(ligados.lista === 'J K' && ligados.busca === '/', `atalhos declarados na tela (aria-keyshortcuts ${JSON.stringify(ligados)})`)
    // Desligado na Ajuda: J não troca de conversa e a tela deixa de declarar os atalhos (WCAG 2.1.4).
    await p.getByRole('checkbox', { name: 'Atalhos de uma tecla' }).uncheck()
    await p.keyboard.press('Escape')
    await p.waitForTimeout(400)
    await abrirConversa(p, 'Marcelo')
    const antes = await atual(p)
    await soltarFoco(p)
    await p.keyboard.press('j')
    await p.waitForTimeout(400)
    const desligados = await declarados()
    cobrar((await atual(p)) === antes && desligados.lista === null && desligados.busca === null, `com o interruptor desligado, J não troca de conversa e nada é declarado (${JSON.stringify(desligados)})`)
    await p.close()
  }
  if (roda('B')) {
    // A 1024 a Ficha é uma gaveta não modal; com ela aberta, J segue trocando de conversa.
    const p = await abrir(1024)
    const lista = await ordem(p)
    await abrirConversa(p, 'Marcelo')
    await p.getByRole('button', { name: /^Ficha de / }).click()
    await p.getByRole('button', { name: 'Fechar Ficha' }).waitFor({ timeout: 5000 })
    const antes = await atual(p)
    await soltarFoco(p)
    await p.keyboard.press('j')
    await p.waitForTimeout(500)
    cobrar((await atual(p)) === lista[lista.indexOf(antes) + 1], '1024 px: com a Ficha aberta, J abre a próxima conversa')
    await p.close()
  }

  if (roda('C')) for (const tema of ['light', 'dark']) for (const comAssistente of [false, true]) {
    const p = await abrir(1024, tema)
    await abrirConversa(p, 'Marcelo')
    const assumir = p.getByRole('button', { name: /^Assumir/ }).first()
    if (await assumir.count()) { await assumir.click(); await p.waitForTimeout(800) }
    if (comAssistente) { await p.getByRole('button', { name: /Abrir assistente/i }).first().click(); await p.waitForTimeout(900) }
    const m = await p.evaluate(() => {
      const r = document.querySelector('[class*=rolagemCentro]')?.getBoundingClientRect()
      return r ? { altura: Math.round(r.height), fracao: Math.round((r.height / innerHeight) * 1000) / 10 } : null
    })
    // Sem arredondar a favor: 55% de 768 são 422,4 px.
    cobrar(Boolean(m) && m.altura >= 0.55 * 768, `1024 x 768 ${tema}, conversa assumida, Assistente ${comAssistente ? 'aberto' : 'fechado'}: mensagens com pelo menos 55% da altura (${JSON.stringify(m)})`)
    await p.close()
  }

  if (roda('D')) for (const w of LARGURAS) {
    const p = await abrir(w)
    await abrirConversa(p, 'Marcelo')
    const m = await p.evaluate(() => {
      const b = [...document.querySelectorAll('button')].find((e) => /^Enviar/.test(e.getAttribute('aria-label') ?? '') && e.disabled)
      if (!b) return null
      const s = getComputedStyle(b)
      return { rotulo: b.getAttribute('aria-label'), borda: s.borderTopStyle === 'none' || parseFloat(s.borderTopWidth) === 0 || s.borderTopColor === 'rgba(0, 0, 0, 0)' ? 'sem' : `${s.borderTopWidth} ${s.borderTopStyle}` }
    })
    cobrar(Boolean(m) && m.borda === 'sem', `${w} px: "Enviar" desabilitado sem contorno (${JSON.stringify(m)})`)
    await p.close()
  }
} finally {
  await navegador.close()
  await servidor.close()
}
console.log(falhas.length ? `\n${falhas.length} falha(s)` : '\ntudo ok')
process.exit(falhas.length ? 1 : 0)
