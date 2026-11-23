/* eslint-disable no-console */
// Prova visual da #3210 (S85): seletor de emoji por categoria, com busca. Componentes reais com CSS real, em Chromium,
// ações de mentira; sem servidor.
//   a) abre com o foco na busca; abas em tablist, uma selecionada; setas esquerda/direita e clique trocam de aba;
//   b) a altura do painel é a mesma em todas as abas; c) a busca acha emoji de outra categoria e tira a seleção das abas;
//   d) sem resultado, "Nenhum emoji encontrado"; e) clique insere no cursor; f) Esc fecha e o foco volta ao campo;
//   g) a 390 px nada rola na horizontal e o painel cabe na janela; h) nenhuma aba destaca por borda colorida de um lado só.
// FASE=antes só tira os prints do seletor atual (sem abas), no código desta pasta.
//
//   node ferramentas/prova-emoji-categorias.mjs
//   Os PNG vão para EVIDENCIAS_3210 (padrão: .build/prova-emoji-categorias/<fase>).
import assert from 'node:assert/strict'
import { mkdirSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'
import { build } from 'vite'
import react from '@vitejs/plugin-react'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const fase = process.env.FASE === 'antes' ? 'antes' : 'depois'
const raiz = fileURLToPath(new URL('..', import.meta.url)).replaceAll('\\', '/').replace(/\/$/, '')
const pasta = resolve(raiz, '.build/prova-emoji-categorias')
const evidencias = resolve(process.env.EVIDENCIAS_3210 ?? resolve(pasta, fase))
mkdirSync(pasta, { recursive: true })
mkdirSync(evidencias, { recursive: true })
const entrada = resolve(pasta, `entrada-${fase}.jsx`)

writeFileSync(entrada, `
import React from 'react'
import { createRoot } from 'react-dom/client'
import { ContextoAcessoModulos } from '${raiz}/src/aplicacao/acessoModulos.jsx'
import { ContextoAcoes, ContextoCatalogo, ContextoEstado } from '${raiz}/src/aplicacao/contextos.js'
import { PainelAtendimento } from '${raiz}/src/features/atendimento/PainelAtendimento.jsx'
import { CANAIS } from '${raiz}/src/infra/catalogo.js'
import moldura from '${raiz}/src/app/moldura.module.css'
import '${raiz}/src/estilos/base.css'

const agora = Date.now()
const noop = () => {}
const conversa = {
  id: 'c1', nome: 'Renan Okamoto', canal: 'WhatsApp', estado: 'Aberto', conta: 'cliente', clienteId: 'cli1',
  cliente: { tags: [], notas: [], pedidos: 2 }, pedido: null, passagem: null,
  mensagens: [{ id: 'm0', dir: 'in', texto: 'Oi, tudo bem?', em: new Date(agora - 60000).toISOString() }],
  ultimaEm: new Date(agora - 60000).toISOString(),
}
const acoes = {
  descarregarRascunho: noop, enviarMidia: noop, definirRascunho: noop, devolverAutomatico: noop, selecionar: noop,
  abrirEncerramento: noop, reabrir: noop, listarAtendentes: async () => [], enviar: noop,
}
const estado = { selecionada: conversa, visiveis: [conversa], agora, automaticoPausado: { c1: false }, rascunho: '', escritaDeFora: 0, fonteApi: true, sincronizacao: {} }
const cat = { janelas: [], canais: CANAIS, respostasProntas: [], cardapio: [], linhas: [], adicionais: [] }
const acesso = { permite: () => true, acoes: { reabrirConversa: true }, aoSair: null }
createRoot(document.getElementById('root')).render(
  <ContextoAcessoModulos value={acesso}><ContextoEstado value={estado}><ContextoAcoes value={acoes}><ContextoCatalogo value={cat}>
    <div style={{ display: 'flex', height: '100dvh' }}>
      <section className={moldura.coluna + ' ' + moldura.centro}>
        <PainelAtendimento aoAbrirNota={noop} aoAbrirGaleria={noop} aoAbrirBiblioteca={noop} />
      </section>
    </div>
  </ContextoCatalogo></ContextoAcoes></ContextoEstado></ContextoAcessoModulos>,
)
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
const estilo = saidas.filter((a) => a.type === 'asset' && a.fileName.endsWith('.css')).map((a) => String(a.source)).join('\n')
const html = `<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${estilo}</style><div id="root"></div><script>${codigo.replaceAll('</script', '<\\/script')}</script></html>`

const ORIGEM = 'http://prova.local'
const TELAS = { desktop: { width: 1440, height: 900 }, celular: { width: 390, height: 844 } }
const T = { timeout: 8000 }
const navegador = await chromium.launch({ headless: true })
const erros = []
const resultado = []
let ultima = null

async function abrir(tela) {
  const ctx = await navegador.newContext({ viewport: TELAS[tela], reducedMotion: 'reduce', serviceWorkers: 'block' })
  await ctx.addInitScript(() => {
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' }, usuario: { id: 'u1' } }))
  })
  await ctx.route(ORIGEM + '/**', (rota) => {
    const url = new URL(rota.request().url())
    if (!url.pathname.startsWith('/api/')) return rota.fulfill({ contentType: 'text/html', body: html })
    return rota.fulfill({ json: { success: true, data: [] } })
  })
  const p = await ctx.newPage()
  ultima = p
  p.on('pageerror', (e) => erros.push('pageerror: ' + e.message))
  await p.goto(`${ORIGEM}/`)
  await p.getByLabel('Mensagem para o cliente', { exact: true }).waitFor(T)
  return p
}
const campo = (p) => p.getByLabel('Mensagem para o cliente', { exact: true })
const dialogo = (p) => p.getByRole('dialog', { name: 'Emoji' })
const abrirSeletor = async (p) => { await p.getByRole('button', { name: 'Emoji', exact: true }).click(); await dialogo(p).waitFor(T) }
const foto = (p, nome) => p.screenshot({ path: resolve(evidencias, `emoji-${nome}.png`) })
const altura = async (p) => Math.round((await dialogo(p).boundingBox()).height)
const selecionada = (p) => p.getByRole('tab', { selected: true }).evaluateAll((ts) => ts.map((t) => t.getAttribute('aria-label')))

async function cenario(nome, corpo) {
  try { await corpo(); resultado.push([nome, 'ok']); console.log('ok    ' + nome) } catch (e) {
    resultado.push([nome, 'FALHA']); console.log('FALHA ' + nome + '\n      ' + String(e.message).split('\n').slice(0, 4).join('\n      '))
    if (ultima) await ultima.screenshot({ path: resolve(evidencias, `falha-${nome.replace(/\W+/g, '-')}.png`) }).catch(() => {})
  }
}

try {
  for (const tela of Object.keys(TELAS)) {
    if (fase === 'antes') {
      await cenario(`antes-${tela}`, async () => {
        const p = await abrir(tela)
        await abrirSeletor(p)
        await foto(p, `antes-${tela}`)
      })
      continue
    }
    await cenario(`a-abas-e-foco-${tela}`, async () => {
      const p = await abrir(tela)
      await abrirSeletor(p)
      assert.ok(await p.getByRole('searchbox', { name: 'Buscar emoji' }).evaluate((e) => e === document.activeElement), 'foco na busca ao abrir')
      assert.equal(await p.getByRole('tablist').count(), 1)
      const abas = await p.getByRole('tab').evaluateAll((ts) => ts.map((t) => t.getAttribute('aria-label')))
      assert.equal(abas.length, 7, 'sete abas')
      assert.deepEqual(await selecionada(p), ['Mais usados'])
      const h0 = await altura(p)
      await foto(p, `depois-mais-usados-${tela}`)
      await p.getByRole('tab', { name: 'Carinhas' }).click()
      assert.deepEqual(await selecionada(p), ['Carinhas'], 'clique troca de aba')
      assert.ok(await p.getByRole('tab', { name: 'Carinhas' }).evaluate((e) => e === document.activeElement), 'foco na aba clicada')
      assert.equal(await altura(p), h0, 'altura igual ao trocar de aba')
      await foto(p, `depois-carinhas-${tela}`)
      await p.keyboard.press('ArrowRight')
      assert.deepEqual(await selecionada(p), [abas[2]], 'seta direita avança')
      await p.keyboard.press('ArrowLeft')
      await p.keyboard.press('ArrowLeft')
      assert.deepEqual(await selecionada(p), [abas[0]], 'seta esquerda volta')
      await p.keyboard.press('ArrowLeft')
      assert.deepEqual(await selecionada(p), [abas.at(-1)], 'a seta dá a volta')
      for (const nome of abas) {
        await p.getByRole('tab', { name: nome, exact: true }).click()
        assert.equal(await altura(p), h0, `altura igual na aba ${nome}`)
        const n = await dialogo(p).getByRole('button').count()
        assert.ok(n >= 20 && n <= 40, `${nome}: ${n} emojis`)
      }
      const umLado = await p.getByRole('tab').evaluateAll((ts) => ts.filter((t) => {
        const c = getComputedStyle(t)
        return ['Top', 'Right', 'Bottom', 'Left'].filter((l) => parseFloat(c['border' + l + 'Width']) > 0 && c['border' + l + 'Style'] !== 'none').length === 1
      }).length)
      assert.equal(umLado, 0, 'aba sem borda de um lado só')
    })
    await cenario(`b-busca-${tela}`, async () => {
      const p = await abrir(tela)
      await abrirSeletor(p)
      const caixa = p.getByRole('searchbox', { name: 'Buscar emoji' })
      const h0 = await altura(p)
      await caixa.fill('pizza')
      assert.deepEqual(await selecionada(p), [], 'com termo, as abas ficam sem seleção')
      assert.ok(await dialogo(p).getByRole('button', { name: /pizza/ }).count() >= 1, 'acha emoji de outra categoria')
      assert.equal(await altura(p), h0, 'altura igual com a busca')
      await foto(p, `depois-busca-${tela}`)
      await caixa.fill(':coracao')
      assert.ok(await dialogo(p).getByRole('button', { name: /coração/ }).count() >= 1, ':nome sem acento')
      await caixa.fill('xyzxyz')
      await dialogo(p).getByText('Nenhum emoji encontrado').waitFor(T)
      assert.equal(await dialogo(p).getByRole('button').count(), 0)
      assert.equal(await altura(p), h0, 'altura igual sem resultado')
      await foto(p, `depois-vazio-${tela}`)
      await caixa.fill('')
      assert.deepEqual(await selecionada(p), ['Mais usados'], 'limpar a busca volta à aba ativa')
    })
    await cenario(`c-insere-e-esc-${tela}`, async () => {
      const p = await abrir(tela)
      await campo(p).fill('Oi tudo')
      await campo(p).evaluate((el) => el.setSelectionRange(2, 2))
      await abrirSeletor(p)
      await p.getByRole('tab', { name: 'Carinhas' }).click()
      await dialogo(p).getByRole('button').first().click()
      const inserido = await campo(p).inputValue()
      assert.ok(inserido.length > 'Oi tudo'.length && inserido.startsWith('Oi') && inserido.endsWith(' tudo'), `insere no cursor: ${inserido}`)
      await p.getByRole('searchbox', { name: 'Buscar emoji' }).fill('pizza')
      await p.keyboard.press('Enter')
      assert.ok((await campo(p).inputValue()).includes('🍕'), 'Enter na busca escolhe o primeiro')
      await p.keyboard.press('Escape')
      await dialogo(p).waitFor({ state: 'detached' })
      await p.waitForFunction(() => document.activeElement?.tagName === 'TEXTAREA', null, T).catch(() => {})
      assert.ok(await campo(p).evaluate((e) => e === document.activeElement), 'o foco volta ao campo de mensagem')
    })
    if (tela === 'celular') {
      await cenario('d-cabe-em-390', async () => {
        const p = await abrir(tela)
        await abrirSeletor(p)
        const r = await p.evaluate(() => ({ sw: document.documentElement.scrollWidth, jan: innerWidth }))
        assert.ok(r.sw <= r.jan, `rolagem horizontal da página: ${r.sw} > ${r.jan}`)
        const caixa = await dialogo(p).boundingBox()
        console.log('      painel 390:', JSON.stringify(caixa)); assert.ok(caixa.x >= 0 && caixa.x + caixa.width <= 390 && caixa.y >= 0, `painel dentro da janela: ${JSON.stringify(caixa)}`)
        const interno = await dialogo(p).evaluate((e) => [...e.querySelectorAll('*')].filter((n) => n.scrollWidth > n.clientWidth + 1 && getComputedStyle(n).overflowX === 'visible').map((n) => n.tagName + '.' + String(n.className).slice(0, 30) + ' ' + n.scrollWidth + '>' + n.clientWidth))
        assert.equal(interno.join(' | '), '', 'nada estoura na horizontal dentro do painel')
        await foto(p, 'depois-celular-aberto')
      })
    }
  }
  assert.deepEqual(erros, [], 'erros de página')
} finally {
  await navegador.close()
}
const falhas = resultado.filter(([, s]) => s !== 'ok')
console.log(`\n${resultado.length - falhas.length}/${resultado.length} cenários ok`)
if (falhas.length || erros.length) { console.log(erros.join('\n')); process.exit(1) }
