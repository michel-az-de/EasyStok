/* eslint-disable no-console */
// Prova visual da #3207 (S82): "Cadastrar cliente" é UM botão no cartão do lead.
//   a. cartão do lead com o botão principal e o vínculo como ação discreta, sem "Conferir cadastros";
//   b. a Ficha abre direto no formulário, com Celular, Endereço e CEP preenchidos e "Salvar cadastro" à vista;
//   c. erro do servidor ao salvar aparece como veio (role="alert");
//   d. salvou: o cartão passa a mostrar o cliente e some o "Cadastrar cliente".
// Componentes reais (PainelFicha) com CSS real, em Chromium, com ações de mentira. Sem servidor: o harness é
// compilado com o Vite e servido pelo próprio Playwright.
//
//   PLAYWRIGHT_MODULE=<pasta do playwright> node ferramentas/prova-cadastrar-cliente-lead.mjs
//   FASE=antes   prints do caminho antigo; precisa de CODIGO_ANTES=<pasta EasyStock.Console do código de antes>,
//                lida sem escrever nada nela (o harness e os PNG ficam aqui).
//   FASE=depois  (padrão) os quatro estados, com asserções. Sai com exit 1 se uma falhar.
//   Os PNG vão para EVIDENCIAS_3207 (padrão: .build/prova-cadastrar-cliente-lead/<fase>).
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
const pasta = resolve(aqui, '.build/prova-cadastrar-cliente-lead')
const evidencias = resolve(process.env.EVIDENCIAS_3207 ?? resolve(pasta, fase))
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

const LEAD = {
  id: 'c1', nome: 'Marina Souza', canal: 'WhatsApp', estado: 'Aberto', conta: 'lead', clienteId: null,
  contato: '5511987654321', mensagens: [], pedido: null, passagem: null, ultimaEm: new Date(agora).toISOString(),
  cliente: { tags: [], notas: [], pedidos: 0, telefone: '', endereco: '', enderecoCapturado: 'Rua das Flores, 412, apto 71, Centro, 01310-100' },
}
const CADASTRADO = {
  ...LEAD, conta: 'cliente', clienteId: 'cli1',
  cliente: { tags: [], notas: [], pedidos: 0, telefone: '(11) 98765-4321', endereco: 'Rua das Flores, 412, apto 71, Centro, 01310-100' },
}

function Cena() {
  const [conversa, setConversa] = useState(LEAD)
  const acoes = useMemo(() => new Proxy({
    salvarCadastroRapido: async (id, dados) => {
      ordem.push('salvar:' + JSON.stringify(dados))
      await espera(150)
      if (q.get('erro') === '1') return { erro: 'Esse telefone já pertence a outro cadastro. Confira em Cadastros antes de criar outro.' }
      setConversa(CADASTRADO)
      return {}
    },
  }, { get: (alvo, nome) => (nome in alvo ? alvo[nome] : async () => ({})) }), [])
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
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' }, usuario: { id: 'u1' } }))
  })
  await ctx.route(ORIGEM + '/**', (rota) => {
    const url = new URL(rota.request().url())
    if (!url.pathname.startsWith('/api/')) return rota.fulfill({ contentType: 'text/html', body: html })
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

try {
  if (fase === 'antes') {
    for (const tela of Object.keys(TELAS)) {
      const p = await abrir(tela)
      const rotulos = await p.getByRole('button').evaluateAll((bs) => bs.map((b) => (b.getAttribute('aria-label') || b.textContent || '').trim()).filter(Boolean))
      console.log(`antes ${tela}: botões = ${JSON.stringify(rotulos)}`)
      await foto(p, `antes-a-cartao-${tela}`)
      await p.getByRole('button', { name: /Abrir ficha de/ }).click()
      await p.getByRole('dialog').waitFor(T)
      await foto(p, `antes-b-ficha-${tela}`)
      await p.context().close()
    }
  }
  if (fase === 'depois') {
    for (const tela of Object.keys(TELAS)) {
      // (a) cartão do lead: um botão principal e o vínculo discreto
      let p = await abrir(tela)
      const cadastrar = p.getByRole('button', { name: 'Cadastrar cliente', exact: true })
      await cadastrar.waitFor(T)
      assert.equal(await cadastrar.count(), 1)
      assert.equal(await cadastrar.getAttribute('data-cadastrar-cliente') !== null, true)
      await p.getByRole('button', { name: 'Já é cliente? Vincular a um cadastro' }).waitFor(T)
      assert.equal(await p.getByRole('button', { name: /Conferir cadastros/ }).count(), 0)
      assert.equal(await p.getByRole('button', { name: /Conferir identidade/ }).count(), 0)
      assert.equal(await p.getByText('Contato sem cadastro').isVisible(), true)
      await cadastrar.scrollIntoViewIfNeeded()
      assert.ok(await cadastrar.isVisible())
      await semRolagemLateral(p)
      await foto(p, `depois-a-cartao-${tela}`)
      // (b) a Ficha abre no formulário, preenchida com o que a conversa captou
      await cadastrar.click()
      const ficha = p.getByRole('dialog')
      await ficha.waitFor(T)
      const celular = ficha.getByLabel('Celular')
      await celular.waitFor(T)
      assert.equal(await celular.inputValue(), '(11) 98765-4321')
      assert.match(await ficha.getByLabel('Endereço').inputValue(), /Rua das Flores, 412/)
      assert.equal(await ficha.getByLabel('CEP').inputValue(), '01310-100')
      const salvar = p.getByRole('button', { name: 'Salvar cadastro' })
      await salvar.waitFor(T)
      const caixa = await salvar.boundingBox()
      assert.ok(caixa && caixa.y >= 0 && caixa.y + caixa.height <= TELAS[tela].height, 'Salvar cadastro fora da tela')
      // Um caminho só: aberta pelo botão, a Ficha não repete o "Confirmar e cadastrar" do endereço capturado.
      assert.equal(await p.getByRole('button', { name: 'Confirmar e cadastrar' }).count(), 0, 'dois botões de cadastrar na mesma Ficha')
      await semRolagemLateral(p)
      await foto(p, `depois-b-ficha-${tela}`)
      await p.context().close()
      // (c) erro do servidor ao salvar, como veio
      p = await abrir(tela, 'erro=1')
      await p.getByRole('button', { name: 'Cadastrar cliente', exact: true }).click()
      await p.getByRole('dialog').getByLabel('Celular').waitFor(T)
      await p.getByRole('button', { name: 'Salvar cadastro' }).click()
      const alerta = p.getByRole('alert').filter({ hasText: 'Esse telefone já pertence a outro cadastro' })
      await alerta.waitFor(T)
      assert.ok(await alerta.isVisible())
      assert.equal(await p.getByRole('dialog').count(), 1, 'a Ficha deve continuar aberta no erro')
      await p.waitForTimeout(300)
      const caixaDoErro = await alerta.boundingBox()
      assert.ok(caixaDoErro && caixaDoErro.y >= 0 && caixaDoErro.y + caixaDoErro.height <= TELAS[tela].height, 'o erro deve estar à vista, sem rolar')
      await foto(p, `depois-c-erro-${tela}`)
      await p.context().close()
      // (d) salvou: a Ficha fecha e o cartão mostra o cliente
      p = await abrir(tela)
      await p.getByRole('button', { name: 'Cadastrar cliente', exact: true }).click()
      await p.getByRole('dialog').getByLabel('Celular').waitFor(T)
      await p.getByRole('button', { name: 'Salvar cadastro' }).click()
      await p.getByRole('dialog').waitFor({ state: 'hidden', ...T })
      assert.match(await p.evaluate(() => window.ordem.join('|')), /salvar:.*11987654321|salvar:.*\(11\) 98765-4321/)
      assert.equal(await p.getByRole('button', { name: 'Cadastrar cliente', exact: true }).count(), 0)
      assert.equal(await p.getByText('Contato sem cadastro').count(), 0)
      await p.getByRole('button', { name: /Abrir ficha de Marina/ }).waitFor(T)
      await semRolagemLateral(p)
      await foto(p, `depois-d-cadastrado-${tela}`)
      await p.context().close()
      console.log('ok    ' + tela)
    }
  }
  assert.deepEqual(erros, [])
} catch (falha) {
  await ultima?.screenshot({ path: resolve(evidencias, 'falha.png') }).catch(() => {})
  if (erros.length) console.error('erros na página:\n' + erros.join('\n'))
  throw falha
} finally { await navegador.close() }
