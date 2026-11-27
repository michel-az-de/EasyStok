/* eslint-disable no-console */
// Prova visual da #3265, fatia B2 (R1 e R2 da crítica do Atendimento, docs/dev/critica-atendimento-3234-2026-11-23.md).
//   R1  as ações do cartão do cliente (copiar telefone, copiar endereço, abrir no mapa e conferir identidade) ficam numa
//       fileira só, logo abaixo da identidade, cada uma com ícone e rótulo curto visível; nada solto fora dela.
//   R2  "Itens" só é primário com pedido em montagem; sem pedido é secundário.
// Componentes reais (PainelFicha) com CSS real, em Chromium, sem servidor. Harness da prova-quem-e (#3209).
//
//   PLAYWRIGHT_MODULE=<pasta do playwright> node ferramentas/prova-3265-cartao-acoes.mjs
//   FASE=antes   prints do código de antes; precisa de CODIGO_ANTES=<pasta EasyStock.Console de antes>, só leitura.
//   FASE=depois  (padrão) cenários com asserções. Sai com exit 1 se uma falhar.
//   Os PNG vão para EVIDENCIAS_3265 (padrão: .build/prova-3265-cartao-acoes/<fase>).
import assert from 'node:assert/strict'
import { mkdirSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'
import { build } from 'vite'
import react from '@vitejs/plugin-react'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const fase = process.env.FASE === 'antes' ? 'antes' : 'depois'
const aqui = fileURLToPath(new URL('..', import.meta.url)).replaceAll('\\', '/').replace(/\/$/, '')
const raiz = fase === 'antes' ? String(process.env.CODIGO_ANTES ?? '').replaceAll('\\', '/').replace(/\/$/, '') : aqui
if (!raiz) throw new Error('FASE=antes pede CODIGO_ANTES=<pasta EasyStock.Console do código de antes>')
const pasta = resolve(aqui, '.build/prova-3265-cartao-acoes')
const evidencias = resolve(process.env.EVIDENCIAS_3265 ?? resolve(pasta, fase))
mkdirSync(pasta, { recursive: true })
mkdirSync(evidencias, { recursive: true })
const entrada = resolve(pasta, `entrada-${fase}.jsx`)

const ENDERECO = 'Rua das Flores, 412, Centro, 01310-100'
const TELEFONE = '(11) 98765-4321'

writeFileSync(entrada, `
import React, { useMemo, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { ContextoAcessoModulos } from '${raiz}/src/aplicacao/acessoModulos.jsx'
import { ContextoAcoes, ContextoCatalogo, ContextoEstado } from '${raiz}/src/aplicacao/contextos.js'
import { PainelFicha } from '${raiz}/src/features/ficha-cliente/PainelFicha.jsx'
import { CANAIS } from '${raiz}/src/infra/catalogo.js'
import '${raiz}/src/estilos/base.css'

const q = new URLSearchParams(location.search)
const agora = Date.now()
const noop = () => {}

const mid = (id, em) => ({ id, dir: 'in', texto: 'Foto', em, midia: { conversaId: 'c1', mensagemId: id, mime: 'image/jpeg' } })
const BASE = {
  id: 'c1', nome: 'Marina Souza', canal: 'WhatsApp', estado: 'Aberto', conta: 'lead', clienteId: null,
  contato: '5511987654321', mensagens: [], pedido: null, passagem: null, ultimaEm: new Date(agora).toISOString(),
  cliente: { tags: [], notas: [], pedidos: 0, telefone: '', endereco: '' },
}
const CLIENTE = {
  ...BASE, conta: 'cliente', clienteId: 'cli1', relacionamento: 'cliente', temAnteriores: true,
  mensagens: Array.from({ length: 3 }, (_, i) => mid('img' + i, agora - 100000 + i * 1000)),
  cliente: { tags: [], notas: [], pedidos: 3, totalGasto: 140, telefone: '${TELEFONE}', endereco: '${ENDERECO}',
    historico: [{ numero: '#a', em: '2026-10-01', total: 40, estado: 'entregue', itens: [] }, { numero: '#b', em: '2026-11-12', total: 54.5, estado: 'entregue', itens: [] }] },
}
const CENAS = {
  cliente: CLIENTE,
  semendereco: { ...CLIENTE, cliente: { ...CLIENTE.cliente, endereco: '' } },
  montagem: { ...CLIENTE, pedido: { estado: 'aguardando', itens: [{ id: 'i1', produtoId: 'p1', nome: 'Bolo', qtd: 2, preco: 15 }], totalApi: 30 } },
  leadwa: BASE,
  lid: { ...BASE, contato: '203645728109577' },
}
const CENA = CENAS[q.get('cena') ?? 'cliente']
const largura = q.get('largura') === 'cheia' ? '100%' : Number(q.get('largura') ?? 316)

function Cena() {
  const [conversa] = useState(CENA)
  const acoes = useMemo(() => new Proxy({}, { get: (alvo, nome) => (nome in alvo ? alvo[nome] : async () => ({})) }), [])
  const estado = {
    selecionada: conversa, visiveis: [conversa], conversas: [conversa], historico: [], agora, automaticoPausado: { c1: false },
    fonteApi: true, sessao: { usuario: { id: 'u1' } }, alertasEstoque: [], fidelidade: { cupons: [], resgatesPorCadastro: {} },
    rascunho: '', escritaDeFora: 0,
  }
  const cat = { janelas: [], canais: CANAIS, respostasProntas: [], cardapio: [], linhas: [], adicionais: [], restricoes: [], motivosBloqueio: [], prefixosCepAtendidos: [] }
  const acesso = { permite: () => true, acoes: { editarAtendimento: true, corrigirVinculoClienteConversa: true }, aoSair: null }
  return (
    <ContextoAcessoModulos value={acesso}><ContextoEstado value={estado}><ContextoAcoes value={acoes}><ContextoCatalogo value={cat}>
      <div style={{ display: 'flex', justifyContent: 'flex-end', height: '100dvh', background: 'var(--fundo)' }}>
        <aside style={{ width: largura, height: '100%', overflow: 'auto', background: 'var(--superficie, var(--fundo))' }}>
          <PainelFicha aoAbrirCardapio={noop} aoAbrirNota={noop} aoRascunho={noop} />
        </aside>
      </div>
    </ContextoCatalogo></ContextoAcoes></ContextoEstado></ContextoAcessoModulos>
  )
}
createRoot(document.getElementById('root')).render(<Cena />)
`)

process.env.VITE_FONTE_DADOS = 'api'
const compilado = await build({
  root: raiz, configFile: false, logLevel: 'silent', cacheDir: resolve(pasta, '.cache-vite'),
  define: { 'process.env.NODE_ENV': '"production"' },
  resolve: { dedupe: ['react', 'react-dom'] },
  plugins: [react()],
  build: { write: false, minify: false, cssCodeSplit: false, lib: { entry: entrada, formats: ['iife'], name: 'Prova' } },
})
const saidas = (Array.isArray(compilado) ? compilado[0] : compilado).output
const codigo = saidas.find((a) => a.type === 'chunk').code
const estilo = String(saidas.find((a) => a.type === 'asset' && a.fileName.endsWith('.css'))?.source ?? '')
const html = `<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${estilo}</style><div id="root"></div><script>${codigo.replaceAll('</script', '<\\/script')}</script></html>`

const ORIGEM = 'http://prova.local'
const navegador = await chromium.launch({ headless: true })
const erros = []
let ultima = null
// A Ficha mede 316 px em 1440 e 490 px em 1920 (padrão da #3265 B1); 292 é o mínimo da coluna.
const TELAS = {
  'desktop-1440-claro': { viewport: { width: 1440, height: 900 }, largura: 316, colorScheme: 'light' },
  'desktop-1920-escuro': { viewport: { width: 1920, height: 1080 }, largura: 490, colorScheme: 'dark' },
  'desktop-minimo': { viewport: { width: 1280, height: 800 }, largura: 292, colorScheme: 'light' },
  celular: { viewport: { width: 390, height: 844 }, largura: 'cheia', colorScheme: 'light' },
}

async function abrir(tela, cena) {
  const { viewport, largura, colorScheme } = TELAS[tela]
  const ctx = await navegador.newContext({ viewport, colorScheme, reducedMotion: 'reduce', serviceWorkers: 'block' })
  await ctx.addInitScript(() => {
    // Origem http não tem área de transferência: um stub guarda o que seria copiado.
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: async (t) => { window.copiado = t } }, configurable: true })
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' }, usuario: { id: 'u1' } }))
  })
  await ctx.route(ORIGEM + '/**', (rota) => {
    const url = new URL(rota.request().url())
    if (!url.pathname.startsWith('/api/')) return rota.fulfill({ contentType: 'text/html', body: html })
    if (/\/midia$/.test(url.pathname)) {
      return rota.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="640" height="480"><rect width="640" height="480" fill="#0f766e"/></svg>' })
    }
    return rota.fulfill({ json: { success: true, data: [] } })
  })
  const pagina = await ctx.newPage()
  ultima = pagina
  pagina.on('pageerror', (e) => erros.push('pageerror: ' + e.message + String(e.stack).split('\n').slice(1, 4).join(' | ')))
  pagina.on('console', (m) => { if (m.type() === 'error') erros.push('console: ' + m.text().slice(0, 300)) })
  await pagina.goto(`${ORIGEM}/?cena=${cena}&largura=${largura}`)
  await pagina.getByRole('button', { name: /Abrir ficha de/ }).waitFor({ timeout: 8000 })
  return pagina
}
const foto = (pagina, nome) => pagina.screenshot({ path: resolve(evidencias, `${nome}.png`) })
const T = { timeout: 8000 }

// Lê a fileira: nome acessível, rótulo visível, se tem ícone, se o rótulo cabe e a linha de cada controle.
const lerFileira = (fileira) => fileira.evaluate((el) => [...el.querySelectorAll('button, a')].map((c) => {
  const r = c.getBoundingClientRect()
  return {
    nome: (c.getAttribute('aria-label') ?? c.textContent).trim(),
    rotulo: c.innerText.trim(),
    icone: Boolean(c.querySelector('svg')),
    cabe: c.scrollWidth <= c.clientWidth + 1,
    topo: Math.round(r.top),
    altura: Math.round(r.height),
  }
}))
const livre = (localizador) => localizador.evaluate((el) => {
  const r = el.getBoundingClientRect()
  const dentro = r.left >= 0 && r.top >= 0 && r.right <= innerWidth && r.bottom <= innerHeight
  const topo = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2)
  return dentro && Boolean(topo) && el.contains(topo)
})
// Primário = preenchido com a cor de ação do tema. Uma sonda resolve var(--acao) no mesmo formato do computado.
const ehPrimario = (localizador) => localizador.evaluate((el) => {
  const sonda = document.createElement('span')
  sonda.style.background = 'var(--acao)'
  document.body.append(sonda)
  const acao = getComputedStyle(sonda).backgroundColor
  sonda.remove()
  return getComputedStyle(el).backgroundColor === acao
})
const segue = async (antes, depois) => antes.evaluate((a, d) => Boolean(a.compareDocumentPosition(d) & Node.DOCUMENT_POSITION_FOLLOWING), await depois.elementHandle())

const ROTULOS = { telefone: 'Telefone', endereco: 'Endereço', mapa: 'Mapa', identidade: 'Identidade' }
const ESPERADO = {
  cliente: [ROTULOS.telefone, ROTULOS.endereco, ROTULOS.mapa, ROTULOS.identidade],
  semendereco: [ROTULOS.telefone, ROTULOS.identidade],
  montagem: [ROTULOS.telefone, ROTULOS.endereco, ROTULOS.mapa, ROTULOS.identidade],
  leadwa: [ROTULOS.telefone],
  lid: null,
}

try {
  for (const tela of Object.keys(TELAS)) {
    for (const cena of Object.keys(ESPERADO)) {
      const p = await abrir(tela, cena)
      await p.waitForTimeout(400)
      await foto(p, `${fase}-${cena}-${tela}`)
      if (fase === 'antes') { await p.context().close(); continue }
      const cartao = p.locator('section[aria-label^="Pedido de"]')
      const fileira = cartao.locator('[data-acoes-do-cliente]')
      const esperado = ESPERADO[cena]
      if (!esperado) {
        assert.equal(await fileira.count(), 0, 'sem telefone nem endereço, a fileira não aparece')
      } else {
        // R1: uma fileira só, com as ações na ordem, ícone e rótulo curto visível, numa linha só e sem rótulo cortado.
        assert.equal(await fileira.count(), 1, 'uma fileira de ações do cliente')
        const controles = await lerFileira(fileira)
        assert.deepEqual(controles.map((c) => c.rotulo), esperado, 'rótulos visíveis na ordem')
        for (const c of controles) {
          assert.ok(c.icone, `${c.rotulo} sem ícone`)
          assert.ok(c.cabe, `${c.rotulo} com rótulo cortado`)
          assert.ok(c.altura >= 44, `${c.rotulo} com alvo de toque menor que 44 px (${c.altura})`)
          // WCAG 2.5.3: o nome acessível contém o rótulo visível.
          assert.ok(c.nome.toLowerCase().includes(c.rotulo.toLowerCase()), `nome "${c.nome}" não contém "${c.rotulo}"`)
        }
        assert.equal(new Set(controles.map((c) => c.topo)).size, 1, `fileira quebrou em mais de uma linha: ${JSON.stringify(controles)}`)
        for (let i = 0; i < controles.length; i += 1) assert.ok(await livre(fileira.locator('button, a').nth(i)), `${controles[i].rotulo} cortado ou coberto`)
        // Nomes acessíveis de antes continuam valendo (provas 2395, quem-e e 2232 procuram por eles).
        const nomes = controles.map((c) => c.nome)
        assert.equal(nomes[0], `Copiar telefone ${TELEFONE}`)
        if (esperado.includes(ROTULOS.endereco)) assert.deepEqual(nomes.slice(1, 3), ['Copiar endereço', 'Abrir no mapa'])
        if (esperado.includes(ROTULOS.identidade)) assert.equal(nomes.at(-1), 'Conferir identidade')
        // Nada solto: fora da fileira o cartão não tem outro "Copiar", nem link de mapa, nem "Conferir identidade".
        assert.equal(await cartao.getByRole('button', { name: /^Copiar/ }).count(), await fileira.getByRole('button', { name: /^Copiar/ }).count(), 'Copiar solto fora da fileira')
        assert.equal(await cartao.getByRole('link').count(), await fileira.getByRole('link').count(), 'link solto fora da fileira')
        assert.equal(await cartao.getByRole('button', { name: 'Conferir identidade', exact: true }).count(), esperado.includes(ROTULOS.identidade) ? 1 : 0)
        // Logo abaixo da identidade: depois do telefone (e do endereço, se houver), antes do resumo do pedido.
        assert.equal((await p.locator('[data-quem-e-telefone]').textContent()).trim(), TELEFONE, 'o telefone continua como texto')
        assert.ok(await segue(p.locator('[data-quem-e-telefone]'), fileira), 'a fileira vem depois do telefone')
        if (esperado.includes(ROTULOS.endereco)) {
          const endereco = cartao.getByText(ENDERECO, { exact: true })
          assert.equal(await endereco.count(), 1, 'o endereço continua como texto')
          assert.ok(await segue(endereco, fileira), 'a fileira vem depois do endereço')
        }
        assert.ok(await segue(fileira, p.locator('section[aria-label="Resumo do atendimento"]')), 'a fileira vem antes do resumo do pedido')
        // Um retorno só para as cópias.
        assert.equal(await cartao.locator('output').count(), 1, 'um output só de retorno')
        await fileira.getByRole('button', { name: /^Copiar telefone/ }).click()
        await fileira.getByText('Copiado', { exact: true }).waitFor(T)
        assert.equal(await p.evaluate(() => window.copiado), TELEFONE)
        await foto(p, `${fase}-${cena}-copiado-${tela}`)
        if (esperado.includes(ROTULOS.endereco)) {
          await fileira.getByRole('button', { name: 'Copiar endereço' }).click()
          await p.waitForFunction((e) => window.copiado === e, ENDERECO, T)
          const mapa = fileira.getByRole('link', { name: 'Abrir no mapa' })
          assert.match(await mapa.getAttribute('href'), /^https:\/\/www\.google\.com\/maps\/search\/\?api=1&query=Rua%20das%20Flores/)
          assert.equal(await mapa.getAttribute('target'), '_blank')
          assert.equal(await mapa.getAttribute('rel'), 'noopener noreferrer')
        }
        if (esperado.includes(ROTULOS.identidade)) {
          await fileira.getByRole('button', { name: 'Conferir identidade', exact: true }).click()
          const dlg = p.getByRole('dialog', { name: 'Conferir identidade da conversa', exact: true })
          await dlg.waitFor(T)
          await p.keyboard.press('Escape')
          await dlg.waitFor({ state: 'detached', timeout: 8000 })
        }
      }
      if (cena === 'leadwa' || cena === 'lid') {
        // #3207: no lead, "Vincular a um cadastro" segue como ação discreta abaixo de "Cadastrar cliente", fora da fileira.
        assert.equal(await cartao.getByRole('button', { name: 'Já é cliente? Vincular a um cadastro', exact: true }).count(), 1)
        assert.equal(await fileira.getByRole('button', { name: /Vincular/ }).count(), 0)
      }
      // R2: "Itens" primário só com pedido em montagem; sem pedido, secundário.
      const itens = p.getByRole('button', { name: 'Adicionar itens', exact: true })
      assert.equal(await itens.count(), 1)
      assert.equal(await ehPrimario(itens), cena === 'montagem', cena === 'montagem' ? 'pedido em montagem: "Itens" primário' : 'sem pedido: "Itens" não é primário')
      assert.equal(await p.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'rolagem horizontal')
      await p.context().close()
      console.log(`ok    ${tela} ${cena}`)
    }
  }
  assert.deepEqual(erros, [])
} catch (falha) {
  await ultima?.screenshot({ path: resolve(evidencias, 'falha.png') }).catch(() => {})
  if (erros.length) console.error('erros na página:\n' + erros.join('\n'))
  throw falha
} finally { await navegador.close() }
