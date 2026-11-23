/* eslint-disable no-console */
// Prova visual da #3209 (S84): bloco "Quem é" no cartão do cliente (telefone com copiar, pedidos, miniaturas de
// mídia que abrem em tamanho inteiro). Componentes reais (PainelFicha) com CSS real, em Chromium, sem servidor.
//
//   PLAYWRIGHT_MODULE=<pasta do playwright> node ferramentas/prova-quem-e.mjs
//   FASE=antes   prints do código de antes; precisa de CODIGO_ANTES=<pasta EasyStock.Console de antes>, só leitura.
//   FASE=depois  (padrão) cenários com asserções. Sai com exit 1 se uma falhar.
//   Os PNG vão para EVIDENCIAS_3209 (padrão: .build/prova-quem-e/<fase>).
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
const pasta = resolve(aqui, '.build/prova-quem-e')
const evidencias = resolve(process.env.EVIDENCIAS_3209 ?? resolve(pasta, fase))
mkdirSync(pasta, { recursive: true })
mkdirSync(evidencias, { recursive: true })
const entrada = resolve(pasta, `entrada-${fase}.jsx`)

// As importações usam o caminho absoluto da raiz do código, para o Vite achar tudo nela (e só ler).
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
const ordem = []
window.ordem = ordem
const noop = () => {}
const espera = (ms) => new Promise((r) => setTimeout(r, ms))

const mid = (id, em, mime = 'image/jpeg') => ({ id, dir: 'in', texto: 'Foto', em, midia: { conversaId: 'c1', mensagemId: id, mime } })
const IMAGENS = Array.from({ length: 8 }, (_, i) => mid('img' + i, agora - 100000 + i * 1000))
const BASE = {
  id: 'c1', nome: 'Marina Souza', canal: 'WhatsApp', estado: 'Aberto', conta: 'lead', clienteId: null,
  contato: '5511987654321', mensagens: [], pedido: null, passagem: null, ultimaEm: new Date(agora).toISOString(),
  cliente: { tags: [], notas: [], pedidos: 0, telefone: '', endereco: '' },
}
const CLIENTE = {
  ...BASE, conta: 'cliente', clienteId: 'cli1', relacionamento: 'cliente', temAnteriores: true,
  mensagens: [...IMAGENS, { id: 'a1', dir: 'in', texto: 'Áudio', em: agora - 50000, midia: { conversaId: 'c1', mensagemId: 'a1', mime: 'audio/ogg' } }],
  cliente: { tags: [], notas: [], pedidos: 3, totalGasto: 140, telefone: '(11) 98765-4321', endereco: 'Rua das Flores, 412, Centro, 01310-100',
    historico: [{ numero: '#a', em: '2026-10-01', total: 40, estado: 'entregue', itens: [] }, { numero: '#b', em: '2026-11-12', total: 54.5, estado: 'entregue', itens: [] }] },
}
const CENAS = {
  cliente: CLIENTE,
  semmidia: { ...CLIENTE, mensagens: [], temAnteriores: false },
  leadwa: BASE,
  lid: { ...BASE, contato: '203645728109577' },
  leadsite: { ...BASE, canal: 'Site', contato: 'visitante-9f3a' },
}
const CENA = CENAS[q.get('cena') ?? 'cliente']

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
      <div style={{ display: 'flex', justifyContent: 'flex-end', height: '100dvh' }}>
        <aside style={{ width: q.get('largura') === 'cheia' ? '100%' : 420, height: '100%', overflow: 'auto', background: 'var(--cor-fundo, #fff)' }}>
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
const TELAS = { desktop: { width: 1440, height: 900 }, celular: { width: 390, height: 844 } }

async function abrir(tela, consulta = '') {
  const ctx = await navegador.newContext({ viewport: TELAS[tela], reducedMotion: 'reduce', serviceWorkers: 'block' })
  await ctx.addInitScript(() => {
    // Origem http não tem área de transferência: um stub guarda o que seria copiado.
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: async (t) => { window.copiado = t } }, configurable: true })
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' }, usuario: { id: 'u1' } }))
  })
  await ctx.route(ORIGEM + '/**', (rota) => {
    const url = new URL(rota.request().url())
    if (!url.pathname.startsWith('/api/')) return rota.fulfill({ contentType: 'text/html', body: html })
    if (/\/midia$/.test(url.pathname)) {
      const cor = ['#c2410c', '#0f766e', '#1d4ed8', '#a21caf', '#4d7c0f'][Number(url.pathname.match(/img(\d)/)?.[1] ?? 0) % 5]
      return rota.fulfill({ contentType: 'image/svg+xml', body: `<svg xmlns="http://www.w3.org/2000/svg" width="640" height="480"><rect width="640" height="480" fill="${cor}"/><circle cx="320" cy="240" r="120" fill="#fff" opacity=".6"/></svg>` })
    }
    return rota.fulfill({ json: { success: true, data: [] } })
  })
  const pagina = await ctx.newPage()
  ultima = pagina
  pagina.on('pageerror', (e) => erros.push('pageerror: ' + e.message + String(e.stack).split('\n').slice(1, 4).join(' | ')))
  pagina.on('console', (m) => { if (m.type() === 'error') erros.push('console: ' + m.text().slice(0, 300)) })
  await pagina.goto(`${ORIGEM}/?${consulta}${tela === 'celular' ? '&largura=cheia' : ''}`)
  await pagina.getByRole('button', { name: /Abrir ficha de/ }).waitFor({ timeout: 8000 })
  return pagina
}
const foto = (pagina, nome) => pagina.screenshot({ path: resolve(evidencias, `${nome}.png`) })
const semRolagemLateral = async (p) => assert.ok(await p.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), 'rolagem horizontal')
const T = { timeout: 8000 }

const CENARIOS = ['cliente', 'semmidia', 'leadwa', 'lid', 'leadsite']
const semBordaSoNaEsquerda = (p) => p.evaluate(() => [...document.querySelectorAll('aside *')].filter((el) => {
  const c = getComputedStyle(el)
  const l = parseFloat(c.borderLeftWidth) > 0 && c.borderLeftStyle !== 'none'
  const outras = ['Top', 'Right', 'Bottom'].some((lado) => parseFloat(c['border' + lado + 'Width']) > 0 && c['border' + lado + 'Style'] !== 'none')
  return l && !outras
}).map((el) => String(el.className)))
const livre = (localizador) => localizador.evaluate((el) => {
  const r = el.getBoundingClientRect()
  const dentro = r.left >= 0 && r.top >= 0 && r.right <= innerWidth && r.bottom <= innerHeight
  const topo = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2)
  return dentro && Boolean(topo) && el.contains(topo)
})

try {
  for (const tela of Object.keys(TELAS)) {
    for (const cena of CENARIOS) {
      const p = await abrir(tela, 'cena=' + cena)
      await p.waitForTimeout(500)
      await foto(p, `${fase}-${cena}-${tela}`)
      if (fase === 'antes') { console.log(`antes ${tela} ${cena}: quem-e=${await p.locator('[data-quem-e]').count()}`); await p.context().close(); continue }
      const bloco = p.locator('[data-quem-e]')
      await semRolagemLateral(p)
      assert.deepEqual(await semBordaSoNaEsquerda(p), [], 'borda colorida só na esquerda')
      if (cena === 'cliente') {
        assert.equal(await bloco.count(), 1)
        assert.equal((await p.locator('[data-quem-e-telefone]').textContent()).trim(), '(11) 98765-4321')
        assert.match(await p.locator('[data-quem-e-pedidos]').textContent(), /3 pedidos · último em 12\/11, R\$\s54,50/)
        const minis = p.locator('[data-miniatura]')
        assert.equal(await minis.count(), 6)
        assert.equal((await p.locator('[data-quem-e-midias] li').last().textContent()).trim(), '+2')
        assert.match(await p.locator('[data-quem-e-midias]').textContent(), /Só as mensagens já carregadas/)
        await p.waitForFunction(() => document.querySelectorAll('[data-miniatura] img').length === 6, null, T)
        const copiar = p.getByRole('button', { name: /Copiar telefone/ })
        assert.ok(await livre(copiar), 'Copiar telefone cortado ou coberto')
        for (let i = 0; i < 6; i += 1) assert.ok(await livre(minis.nth(i)), `miniatura ${i} cortada ou coberta`)
        await copiar.click()
        await p.waitForTimeout(400)
        await p.getByText('Copiado', { exact: true }).waitFor(T)
        assert.equal(await p.evaluate(() => window.copiado), '(11) 98765-4321')
        await foto(p, `${fase}-cliente-copiado-${tela}`)
        const alvo = minis.nth(1)
        await alvo.click()
        const dlg = p.getByRole('dialog')
        await dlg.waitFor(T)
        await dlg.locator('img').waitFor(T)
        await p.waitForTimeout(300)
        assert.ok(await dlg.locator('img').evaluate((i) => i.complete && i.naturalWidth > 0), 'foto inteira não carregou')
        await foto(p, `${fase}-cliente-foto-inteira-${tela}`)
        await p.keyboard.press('Escape')
        await dlg.waitFor({ state: 'detached', timeout: 8000 })
        assert.ok(await alvo.evaluate((el) => el === document.activeElement), 'o foco não voltou para a miniatura')
        assert.equal(await p.getByRole('button', { name: /Cadastrar cliente/ }).count(), 0)
      }
      if (cena === 'semmidia') {
        assert.equal(await bloco.count(), 1)
        assert.equal(await p.locator('[data-quem-e-midias]').count(), 0, 'sem mídia, o bloco de mídias não aparece')
        assert.equal(await p.locator('[data-quem-e-telefone]').count(), 1)
      }
      if (cena === 'leadwa') {
        assert.equal((await p.locator('[data-quem-e-telefone]').textContent()).trim(), '(11) 98765-4321')
        assert.equal(await p.locator('[data-quem-e-midias]').count(), 0)
        const cad = p.getByRole('button', { name: 'Cadastrar cliente', exact: true })
        assert.equal(await cad.count(), 1)
        assert.ok(await livre(cad), 'Cadastrar cliente cortado ou coberto')
      }
      if (cena === 'lid' || cena === 'leadsite') {
        assert.equal(await bloco.count(), 0, 'sem número, o bloco não aparece')
        assert.equal(await p.getByRole('button', { name: 'Cadastrar cliente', exact: true }).count(), 1)
      }
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
