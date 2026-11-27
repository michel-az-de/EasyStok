/* eslint-disable no-console */
// #3386 (crítica r6, H8): o Assistente aberto não espreme a tela. Medido no navegador, modo demonstração, claro e escuro,
// a 1024, 1280 e 1440, com o Assistente aberto. ITENS=1,2,3,4 roda só alguns.
// 1 restrição do cabeçalho e título do painel numa linha só, perfil inteiro acima do campo;
// 2 no tablet a conversa mantém altura legível e o resumo do cliente não é cortado;
// 3 cabeçalho da conversa assumida (canal, kebab) numa linha só; 4 o pedido tem um formato só ("Pedido 0194").
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const raiz = fileURLToPath(new URL('..', import.meta.url))
const servidor = await createServer({ root: raiz, logLevel: 'error', server: { host: '127.0.0.1', port: 5807, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const falhas = []
const itens = (process.env.ITENS ?? '1,2,3,4').split(',')
const cobrar = (ok, msg) => { console.log(ok ? 'ok   ' : 'FALHA', msg); if (!ok) falhas.push(msg) }
const TELAS = [[1024, 768], [1280, 800], [1440, 900]]
// px de conversa no tablet com o Assistente aberto (Marcelo: campo em duas linhas e aviso do canal). Era 271. O resto até uns 360
// é cromo de fora do painel (trilho de 120 px, Ficha/Simular de 56, Avisos de 50).
const ALTURA_MIN_MENSAGENS = 285

async function pagina(w, h, tema) {
  const p = await navegador.newPage({ viewport: { width: w, height: h } })
  await p.emulateMedia({ colorScheme: tema })
  await p.goto(`${origem}/#/m/atendimento/balcao`, { timeout: 90000 })
  await p.getByRole('button', { name: /^Abrir .+/ }).first().waitFor({ timeout: 30000 })
  return p
}
async function abrirConversa(p, nome) {
  await p.locator('button[data-conversa-id]').filter({ has: p.locator(`text=${nome}`) }).first().click()
  await p.waitForTimeout(400)
}
async function abrirAssistente(p) {
  await p.getByRole('button', { name: 'Abrir assistente' }).first().click()
  await p.locator('section[role="dialog"]').first().waitFor({ timeout: 5000 })
  await p.waitForTimeout(600)
}
// Linhas de um texto: tops distintos dos retângulos de linha do conteúdo do elemento (roda no navegador).
const linhasDe = (el) => {
  const tops = new Set(); const t = document.createTreeWalker(el, NodeFilter.SHOW_TEXT)
  for (let n = t.nextNode(); n; n = t.nextNode()) {
    if (!n.textContent.trim()) continue
    const r = document.createRange(); r.selectNodeContents(n)
    for (const x of r.getClientRects()) if (x.width > 0) tops.add(Math.round(x.top / 6))
  }
  return tops.size
}

try {
  for (const tema of ['light', 'dark']) {
    for (const [w, h] of TELAS) {
      const rot = `${tema} ${w}x${h}`
      if (itens.includes('1') || itens.includes('2')) {
        const p = await pagina(w, h, tema)
        await abrirConversa(p, 'Marcelo')
        await abrirAssistente(p)
        const m = await p.evaluate(`(() => {
          const linhasDe = ${linhasDe.toString()}
          const painel = [...document.querySelectorAll('section[role="dialog"]')].find((s) => s.getBoundingClientRect().width > 0)
          const titulo = painel.querySelector('h2'); const nome = painel.querySelector('h2 + p')
          const achar = (txt) => [...painel.querySelectorAll('*')].filter((e) => e.textContent.trim() === txt).pop()
          const cartao = achar('Perfil do cliente')?.closest('article')
          const corpo = cartao?.closest('div[class*="_corpo_"]')
          const campo = achar('Pergunte ao assistente')
          const numeros = [...painel.querySelectorAll('span')].find((e) => /pedidos?$/.test(e.textContent.trim()) && e.parentElement.textContent.includes('total'))?.parentElement
          const restricao = [...document.querySelectorAll('#conteudo-principal *')].filter((e) => e.getBoundingClientRect().width > 0 && /Sem lactose: confira/.test(e.textContent) && e.children.length < 3 && e.textContent.length < 60).pop()
          const rol = document.querySelector('section[aria-label^="Conversa com"]')
          return {
            linhasTitulo: titulo && linhasDe(titulo), linhasNome: nome && linhasDe(nome),
            linhasRestricao: restricao && linhasDe(restricao),
            perfilFundo: cartao && Math.round(cartao.getBoundingClientRect().bottom), corpoFundo: corpo && Math.round(corpo.getBoundingClientRect().bottom), rolavel: corpo && corpo.scrollHeight - corpo.clientHeight, campoTopo: campo && Math.round(campo.getBoundingClientRect().top),
            recolhido: cartao?.querySelector('button[aria-expanded="false"]') != null, numerosFundo: numeros?.offsetParent && Math.round(numeros.getBoundingClientRect().bottom),
            mensagens: rol && Math.round(rol.getBoundingClientRect().height),
          }
        })()`)
        console.log('medida', rot, JSON.stringify(m))
        if (itens.includes('1')) {
          cobrar(m.linhasTitulo === 1 && m.linhasNome === 1, `item 1: ${rot}: título e nome do painel numa linha (${m.linhasTitulo}/${m.linhasNome})`)
          cobrar(m.linhasRestricao === 1, `item 1: ${rot}: restrição do cabeçalho numa linha (${m.linhasRestricao})`)
          cobrar(m.perfilFundo != null && m.perfilFundo <= m.campoTopo && m.rolavel <= 1, `item 1: ${rot}: perfil do cliente inteiro, sem corte sob o campo (fundo ${m.perfilFundo}, campo ${m.campoTopo}, excesso ${m.rolavel}px)`)
        }
        if (itens.includes('2')) {
          cobrar(m.rolavel <= 1 && (!m.numerosFundo ? m.recolhido : m.numerosFundo <= m.corpoFundo - 8), `item 2: ${rot}: "pedidos · total" inteiro (fundo ${m.numerosFundo}, corpo ${m.corpoFundo})`)
          if (w === 1024) cobrar(m.mensagens >= ALTURA_MIN_MENSAGENS, `item 2: ${rot}: conversa com ${m.mensagens}px (mín. ${ALTURA_MIN_MENSAGENS})`)
        }
        await p.close()
      }
      if (itens.includes('3')) {
        const p = await pagina(w, h, tema)
        await abrirConversa(p, 'Marcelo')
        await p.getByRole('button', { name: /Assumir conversa/ }).first().click().catch(() => {})
        await p.waitForTimeout(600)
        const r = await p.evaluate(`(() => {
          const linhasDe = ${linhasDe.toString()}
          const cab = document.querySelector('[data-cabecalho-conversa]'); const peq = cab.querySelector('h2 small')
          return { linhas: linhasDe(peq), altura: Math.round(cab.getBoundingClientRect().height), kebab: Boolean(cab.querySelector('button[aria-label="Mais opções da conversa"]')) }
        })()`)
        console.log('medida', rot, JSON.stringify(r))
        cobrar(r.linhas === 1, `item 3: ${rot}: linha do canal numa linha (${r.linhas}), cabeçalho ${r.altura}px, kebab ${r.kebab}`)
        await p.close()
      }
      if (itens.includes('4')) {
        // Todo texto e rótulo do Atendimento (fila, cabeçalho, conversa, Ficha, Assistente) com o ano no número do pedido.
        const p = await pagina(w, h, tema)
        const vistos = new Set()
        const colher = async () => {
          // Só rótulo da tela conta: a conversa (role=log), a prévia citada da última mensagem no cartão ('Assistente: Pedido ...'),
          // o link e o copia e cola do Pix são o texto como saiu para o cliente, não rótulo do Atendimento.
          const o = ((await p.evaluate(() => { const r = document.querySelector('#conteudo-principal').cloneNode(true); r.querySelectorAll('[role=log]').forEach((e) => e.remove()); return r.textContent })).match(/.{0,14}\b[Pp]edido (?:n[º°] )?20\d\d-\d{3,4}\b/g) ?? []).filter((x) => !/(Assistente|Você|Cliente): [Pp]edido/.test(x)).map((x) => `texto ${x}`)
          const attr = await p.evaluate(() => [...document.querySelectorAll('#conteudo-principal [title],#conteudo-principal [aria-label]')].filter((e) => !e.closest('[role=log]')).flatMap((e) => ['title', 'aria-label'].map((a) => e.getAttribute(a) ?? '')).filter((x) => /\b20\d\d-\d{3,4}\b/.test(x)).map((x) => `attr ${x.slice(0, 60)}`))
          return [...o, ...attr]
        }
        const n = await p.locator('button[data-conversa-id]').count()
        for (let i = 0; i < Math.min(n, 14); i++) {
          if (i > 0 && !(await p.locator('button[data-conversa-id]').nth(i).isVisible())) break
          await p.locator('button[data-conversa-id]').nth(i).click(); await p.waitForTimeout(250)
          if (i === 0) await abrirAssistente(p)
          for (const x of await colher()) vistos.add(x)
        }
        for (const x of await colher()) vistos.add(x)
        console.log(JSON.stringify([...vistos]))
        cobrar(vistos.size === 0, `item 4: ${rot}: pedido sem o ano na tela de Atendimento (${vistos.size} ocorrências)`)
        await p.close()
      }
    }
  }
} finally {
  await navegador.close(); await servidor.close()
}
if (falhas.length) { console.log(`\n${falhas.length} falha(s)`); process.exit(1) }
console.log('\ntudo ok')
