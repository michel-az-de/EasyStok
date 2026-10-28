/* eslint-disable no-console */
// Prova da #2134 (Onda 0 da #2130, banca B captura 02): responder é assumir.
//   1. com o Assistente atendendo, o estado e o botão Assumir ficam fixos no topo da conversa:
//      não rolam junto das mensagens (antes: y=-228 px em 1280x720);
//   2. enviar com o Assistente atendendo assume ANTES (assumir e depois enviar, nessa ordem) e
//      avisa "Você assumiu a conversa"; se assumir falhar a mensagem NÃO sai, o texto fica no
//      campo e o erro aparece junto do campo; conversa que já é da atendente não assume de novo;
//   3. o completar com Tab tem chave para desligar, por usuário e por navegador: desligado,
//      nenhuma chamada ao provedor sai; a escolha sobrevive ao recarregar e não vale para
//      outro usuário;
//   4. o botão de fechar do painel do cardápio tem nome (aria-label e title).
//
// Componentes reais (PainelAtendimento, Composer, BarraModo, PainelCardapio) com CSS real, em
// Chromium, com ações de mentira que anotam a ordem das chamadas. Nenhum servidor: o roteiro
// compila o harness com o Vite e serve a página pelo próprio Playwright.
//
//   PLAYWRIGHT_MODULE=<pasta do playwright> node ferramentas/prova-2134-assumir-ao-responder.mjs
// Com EVIDENCIAS_2134=<pasta> grava duas capturas.
import assert from 'node:assert/strict'
import { mkdirSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'
import { build } from 'vite'
import react from '@vitejs/plugin-react'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const raiz = fileURLToPath(new URL('..', import.meta.url)).replaceAll('\\', '/').replace(/\/$/, '')
const pasta = resolve(raiz, '.build/prova-2134')
mkdirSync(pasta, { recursive: true })

writeFileSync(resolve(pasta, 'entrada.jsx'), `
import React, { useRef, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { ContextoAcoes, ContextoCatalogo, ContextoEstado } from '/src/aplicacao/contextos.js'
import { PainelAtendimento } from '/src/features/atendimento/PainelAtendimento.jsx'
import { PainelCardapio } from '/src/features/cardapio/PainelCardapio.jsx'
import { CANAIS } from '/src/infra/catalogo.js'
import { PAUSA_POR_ASSUMIR } from '/src/dominio/automatico.js'
import moldura from '/src/app/moldura.module.css'
import '/src/estilos/base.css'

const q = new URLSearchParams(location.search)
const agora = Date.now()
const ordem = []
window.ordem = ordem
window.cena = { assumir: q.get('assumir') ?? 'ok', liberar: null }
const noop = () => {}

function mensagens() {
  const lista = [{ id: 'm0', dir: 'in', texto: 'Oi, tem lasanha hoje?', em: new Date(agora - 3600000).toISOString() }]
  for (let i = 1; i <= 40; i += 1) {
    lista.push({ id: 'm' + i, dir: i % 2 ? 'out' : 'in', texto: 'Mensagem de teste número ' + i + ' para a conversa rolar.', em: new Date(agora - 3000000 + i * 60000).toISOString(), automatica: i % 2 === 1 })
  }
  return lista
}

function Cena() {
  const [pausado, setPausado] = useState(q.get('pausado') === '1' ? PAUSA_POR_ASSUMIR : false)
  const [campo, setCampo] = useState({ texto: '', escritaDeFora: 0 })
  const fixa = useRef(mensagens())
  const conversa = {
    id: 'c1', nome: 'Renan Okamoto', canal: 'WhatsApp', estado: 'Aberto', conta: 'cliente',
    cliente: { tags: [], notas: [], pedidos: 2 }, pedido: null, passagem: null,
    mensagens: fixa.current, ultimaEm: new Date(agora - 60000).toISOString(),
  }
  const acoes = {
    descarregarRascunho: ({ texto }) => setCampo((c) => ({ ...c, texto })),
    enviarMidia: noop, definirRascunho: noop, devolverAutomatico: noop, selecionar: noop,
    abrirEncerramento: noop, reabrir: noop, listarAtendentes: async () => [],
    enviar: (id, texto) => { ordem.push('enviar:' + texto); setCampo((c) => ({ texto: '', escritaDeFora: c.escritaDeFora + 1 })) },
    assumirAtendimento: async () => {
      ordem.push('assumir')
      if (window.cena.assumir === 'lento') await new Promise((r) => { window.cena.liberar = r })
      if (window.cena.assumir === 'falha') return false
      setPausado(PAUSA_POR_ASSUMIR)
      return true
    },
  }
  const estado = {
    selecionada: conversa, visiveis: [conversa], agora, automaticoPausado: { c1: pausado },
    rascunho: campo.texto, escritaDeFora: campo.escritaDeFora, fonteApi: q.get('demo') !== '1',
  }
  const catalogo = { janelas: [], canais: CANAIS, respostasProntas: [], cardapio: [], linhas: [], adicionais: [] }
  return (
    <ContextoEstado value={estado}><ContextoAcoes value={acoes}><ContextoCatalogo value={catalogo}>
      <div style={{ display: 'flex', height: '100dvh' }}>
        {q.get('cena') === 'cardapio'
          ? <PainelCardapio pedido={null} aoFechar={noop} aoEscolher={noop} aoAjustar={noop} />
          : <section className={moldura.coluna + ' ' + moldura.centro}>
              <PainelAtendimento aoAbrirNota={noop} aoAbrirGaleria={noop} aoAbrirBiblioteca={noop} />
            </section>}
      </div>
    </ContextoCatalogo></ContextoAcoes></ContextoEstado>
  )
}
createRoot(document.getElementById('root')).render(<Cena />)
`)

process.env.VITE_FONTE_DADOS = 'api'
const compilado = await build({
  root: raiz, configFile: false, logLevel: 'silent',
  define: { 'process.env.NODE_ENV': '"production"' },
  plugins: [react()],
  build: { write: false, minify: false, cssCodeSplit: false, lib: { entry: '.build/prova-2134/entrada.jsx', formats: ['iife'], name: 'Prova' } },
})
const saidas = (Array.isArray(compilado) ? compilado[0] : compilado).output
const codigo = saidas.find((a) => a.type === 'chunk').code
const estilo = String(saidas.find((a) => a.type === 'asset' && a.fileName.endsWith('.css'))?.source ?? '')
const pagina = `<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${estilo}</style><div id="root"></div><script>${codigo.replaceAll('</script', '<\\/script')}</script></html>`

const ORIGEM = 'http://prova.local'
const navegador = await chromium.launch({ headless: true })
const erros = []
let passou = 0
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok    ' + descricao) } catch (erro) {
    erros.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

// Um contexto = um navegador (o localStorage é do contexto). `usuario` troca a sessão.
async function contexto({ width = 1280, height = 720 } = {}) {
  const ctx = await navegador.newContext({ viewport: { width, height }, reducedMotion: 'reduce', serviceWorkers: 'block' })
  await ctx.addInitScript(() => {
    const u = new URLSearchParams(location.search).get('u') ?? 'usuario-1'
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' }, usuario: { id: u } }))
  })
  const chamadasIA = []
  await ctx.route(ORIGEM + '/**', async (rota) => {
    const url = new URL(rota.request().url())
    if (!url.pathname.startsWith('/api/')) return rota.fulfill({ contentType: 'text/html', body: pagina })
    if (url.pathname.endsWith('/assistente')) {
      const corpo = rota.request().postDataJSON()
      chamadasIA.push(corpo)
      const trecho = corpo.pergunta.split('\nTRECHO:\n')[1] ?? ''
      return rota.fulfill({ json: { success: true, data: { resposta: '', acoes: [{ tipo: 'rascunho', texto: trecho + 'confirmar os detalhes?' }] } } })
    }
    return rota.fulfill({ json: { success: true, data: [] } })
  })
  return { ctx, chamadasIA }
}
async function abrir(ctx, consulta = '') {
  const pagina = await ctx.newPage()
  pagina.on('pageerror', (e) => erros.push('pageerror: ' + e.message))
  await pagina.goto(`${ORIGEM}/?${consulta}`)
  await pagina.getByLabel('Mensagem para o cliente', { exact: true }).waitFor()
  return pagina
}
const ordemDe = (pagina) => pagina.evaluate(() => window.ordem.slice())

try {
  // --- 1. estado e Assumir fixos no topo -------------------------------------------------------
  for (const [width, height] of [[1280, 720], [390, 844]]) {
    await confere(`1. ${width}x${height}: o Assumir continua à vista com a conversa rolada até o fim`, async () => {
      const { ctx } = await contexto({ width, height })
      const p = await abrir(ctx)
      await p.evaluate(() => { const r = document.querySelector('[role="log"]').parentElement; r.scrollTop = r.scrollHeight })
      const caixa = await p.getByRole('button', { name: 'Assumir', exact: true }).boundingBox()
      assert.ok(caixa, 'o botão Assumir não existe')
      if (process.env.EVIDENCIAS_2134) await p.screenshot({ path: resolve(process.env.EVIDENCIAS_2134, `assumir-fixo-${width}.png`) })
      assert.ok(caixa.y >= 0 && caixa.y + caixa.height <= height, `Assumir fora da tela: y=${Math.round(caixa.y)} (janela de ${height} px)`)
      const faixa = await p.getByRole('region', { name: 'Quem responde esta conversa' }).evaluate((el) => !document.querySelector('[role="log"]').parentElement.contains(el))
      assert.equal(faixa, true, 'a faixa de quem responde está dentro da área que rola')
      await ctx.close()
    })
  }

  // --- 2. responder é assumir -------------------------------------------------------------------
  await confere('2. enviar com o Assistente atendendo assume antes e avisa "Você assumiu a conversa"', async () => {
    const { ctx } = await contexto()
    const p = await abrir(ctx)
    const campo = p.getByLabel('Mensagem para o cliente', { exact: true })
    await campo.fill('Oi Renan, já te respondo por aqui')
    await campo.press('Enter')
    await p.waitForFunction(() => window.ordem.length >= 2, null, { timeout: 4000 }).catch(() => {})
    assert.deepEqual(await ordemDe(p), ['assumir', 'enviar:Oi Renan, já te respondo por aqui'], 'a ordem precisa ser assumir e depois enviar')
    await p.getByText('Você assumiu a conversa', { exact: true }).waitFor({ timeout: 2000 })
    if (process.env.EVIDENCIAS_2134) await p.screenshot({ path: resolve(process.env.EVIDENCIAS_2134, 'assumiu-ao-enviar.png') })
    assert.equal(await campo.inputValue(), '', 'o campo esvazia depois do envio')
    await ctx.close()
  })

  await confere('2. o botão Enviar (clique) também assume antes', async () => {
    const { ctx } = await contexto()
    const p = await abrir(ctx)
    await p.getByLabel('Mensagem para o cliente', { exact: true }).fill('Pode ser às 12h')
    await p.getByRole('button', { name: 'Enviar', exact: true }).click()
    await p.waitForFunction(() => window.ordem.length >= 2, null, { timeout: 4000 }).catch(() => {})
    assert.deepEqual(await ordemDe(p), ['assumir', 'enviar:Pode ser às 12h'])
    await ctx.close()
  })

  await confere('2. se assumir falhar a mensagem NÃO sai, o texto fica no campo e o erro aparece junto do campo', async () => {
    const { ctx } = await contexto()
    const p = await abrir(ctx, 'assumir=falha')
    const campo = p.getByLabel('Mensagem para o cliente', { exact: true })
    await campo.fill('Não deve sair')
    await campo.press('Enter')
    await p.getByRole('alert').filter({ hasText: /não saiu/i }).waitFor({ timeout: 3000 })
    assert.deepEqual(await ordemDe(p), ['assumir'], 'a mensagem saiu mesmo sem assumir')
    assert.equal(await campo.inputValue(), 'Não deve sair', 'o texto precisa continuar no campo')
    const aviso = await p.getByRole('alert').filter({ hasText: /não saiu/i }).boundingBox()
    const caixaCampo = await campo.boundingBox()
    assert.ok(Math.abs(aviso.y - caixaCampo.y) < 200, 'o erro precisa estar junto do campo')
    assert.equal(await p.getByText('Você assumiu a conversa').count(), 0, 'não pode dizer que assumiu')
    await ctx.close()
  })

  await confere('2. conversa que já é da atendente não assume de novo', async () => {
    const { ctx } = await contexto()
    const p = await abrir(ctx, 'pausado=1')
    const campo = p.getByLabel('Mensagem para o cliente', { exact: true })
    await campo.fill('Segue o link')
    await campo.press('Enter')
    await p.waitForFunction(() => window.ordem.length >= 1, null, { timeout: 4000 }).catch(() => {})
    assert.deepEqual(await ordemDe(p), ['enviar:Segue o link'])
    assert.equal(await p.getByText('Você assumiu a conversa').count(), 0)
    await ctx.close()
  })

  await confere('2. Enter repetido durante o assumir lento assume uma vez só e envia uma vez só', async () => {
    const { ctx } = await contexto()
    const p = await abrir(ctx, 'assumir=lento')
    const campo = p.getByLabel('Mensagem para o cliente', { exact: true })
    await campo.fill('Uma vez só')
    await campo.press('Enter')
    await campo.press('Enter')
    await p.waitForTimeout(300)
    assert.deepEqual(await ordemDe(p), ['assumir'], 'enquanto assume nada é enviado e não assume em dobro')
    await p.evaluate(() => window.cena.liberar())
    await p.waitForFunction(() => window.ordem.length >= 2, null, { timeout: 4000 }).catch(() => {})
    assert.deepEqual(await ordemDe(p), ['assumir', 'enviar:Uma vez só'])
    await ctx.close()
  })

  // --- 3. chave do completar com Tab ------------------------------------------------------------
  const texto = 'Bom dia! Podemos '
  await confere('3. ligado (padrão): digitar chama o provedor; desligar na chave corta a chamada, lembra e é por usuário', async () => {
    const { ctx, chamadasIA } = await contexto()
    const p = await abrir(ctx, 'pausado=1&u=usuario-1')
    const campo = p.getByLabel('Mensagem para o cliente', { exact: true })
    await campo.fill(texto)
    await p.getByRole('button', { name: 'Usar sugestão' }).waitFor({ timeout: 6000 })
    assert.equal(chamadasIA.length, 1, 'ligado: uma chamada ao provedor')

    const chave = p.getByRole('button', { name: /completar com tab/i })
    assert.equal(await chave.count(), 1, 'falta a chave para desligar o completar com Tab')
    assert.equal(await chave.getAttribute('aria-pressed'), 'true')
    await chave.click()
    assert.equal(await chave.getAttribute('aria-pressed'), 'false')
    assert.equal(await p.getByRole('button', { name: 'Usar sugestão' }).count(), 0, 'a sugestão em tela some ao desligar')

    chamadasIA.length = 0
    await campo.fill(texto + 'confirmar o horário do pedido de hoje')
    await p.waitForTimeout(2600)
    assert.equal(chamadasIA.length, 0, 'desligado: nenhuma chamada ao provedor')

    const p2 = await abrir(ctx, 'pausado=1&u=usuario-1')
    assert.equal(await p2.getByRole('button', { name: /completar com tab/i }).getAttribute('aria-pressed'), 'false', 'a escolha sobrevive ao recarregar')
    const p3 = await abrir(ctx, 'pausado=1&u=usuario-2')
    assert.equal(await p3.getByRole('button', { name: /completar com tab/i }).getAttribute('aria-pressed'), 'true', 'outro usuário no mesmo navegador segue com o padrão')
    await ctx.close()
  })

  // --- 4. fechar do cardápio com nome -----------------------------------------------------------
  await confere('4. o botão de fechar do cardápio tem aria-label e title', async () => {
    const { ctx } = await contexto()
    const p = await ctx.newPage()
    p.on('pageerror', (e) => erros.push('pageerror: ' + e.message))
    await p.goto(`${ORIGEM}/?cena=cardapio`)
    const botao = p.getByRole('button', { name: 'Fechar cardápio', exact: true })
    await botao.waitFor({ timeout: 4000 })
    assert.equal(await botao.getAttribute('aria-label'), 'Fechar cardápio')
    assert.equal(await botao.getAttribute('title'), 'Fechar cardápio')
    await ctx.close()
  })
} finally {
  await navegador.close()
}

const pageerrors = erros.filter((e) => e.startsWith('pageerror'))
if (pageerrors.length) console.log('Erros de página:\n' + pageerrors.join('\n'))
if (erros.length) {
  console.log(`\n${erros.length} falha(s), ${passou} ok`)
  process.exit(1)
}
console.log(`\nprova-2134: ${passou} conferências ok`)
