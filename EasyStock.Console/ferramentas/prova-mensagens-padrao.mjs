/* eslint-disable no-console */
// Prova da #3206 (S81): mensagem padrão no botão Respostas do compositor e em Gestão > Respostas.
//   1. janela fechada: Respostas lista as mensagens padrão (nome legível e prévia), sem modal;
//   2. escolher uma mostra um campo por {{n}}, a prévia ao vivo e o Enviar, no próprio compositor; o envio usa o
//      endpoint de modelo e o erro do servidor aparece como veio;
//   3. lista vazia: orienta, com caminho para Gestão > Respostas (gerência) ou pedido à gerência (operador);
//   4. lead sem cadastro também envia (o servidor decide);
//   5. Gestão > Respostas: criar, editar, arquivar, validação em PT-BR e sugestões prontas;
//   6. o seletor do Programar mensagem só mostra modelo da Meta.
// Componentes reais (PainelAtendimento, AbaRespostas) com CSS real, em Chromium, com ações de mentira. Nenhum
// servidor: o harness é compilado com o Vite e servido pelo próprio Playwright.
//
//   PLAYWRIGHT_MODULE=<pasta do playwright> node ferramentas/prova-mensagens-padrao.mjs
//   FASE=antes  só o print do caminho antigo (a modal); só roda no código de antes da #3206.
//   FASE=depois (padrão) painel no compositor (lista, escolha com prévia, envio, vazio, erro) e Gestão (lista e formulário).
//   Os PNG vão para EVIDENCIAS_3206 (padrão: .build/prova-mensagens-padrao/<fase>).
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
const pasta = resolve(raiz, '.build/prova-mensagens-padrao')
const evidencias = resolve(process.env.EVIDENCIAS_3206 ?? resolve(pasta, fase))
mkdirSync(pasta, { recursive: true })
mkdirSync(evidencias, { recursive: true })

writeFileSync(resolve(pasta, 'entrada.jsx'), `
import React, { useMemo, useRef, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { ContextoAcessoModulos } from '/src/aplicacao/acessoModulos.jsx'
import { ContextoAcoes, ContextoCatalogo, ContextoEstado } from '/src/aplicacao/contextos.js'
import { PainelAtendimento } from '/src/features/atendimento/PainelAtendimento.jsx'
import { AbaRespostas } from '/src/features/gestao/respostas/AbaRespostas.jsx'
import { CANAIS } from '/src/infra/catalogo.js'
import moldura from '/src/app/moldura.module.css'
import '/src/estilos/base.css'

const q = new URLSearchParams(location.search)
const agora = Date.now()
const ordem = []
window.ordem = ordem
const noop = () => {}
const espera = (ms) => new Promise((r) => setTimeout(r, ms))

const MODELOS = [
  { nome: 'pedido_pronto_retirada', idioma: 'pt_BR', categoria: 'UTILITY', origem: 'Local', quantidadeParametros: 2,
    corpo: 'Olá, {{1}}! Seu pedido {{2}} está pronto para retirada na loja.', rodape: 'Casa da Baba' },
  { nome: 'confirmar_endereco', idioma: 'pt_BR', categoria: 'UTILITY', origem: 'Local', quantidadeParametros: 1,
    corpo: 'Oi, {{1}}! Para entregar seu pedido, pode confirmar o endereço de entrega?' },
  { nome: 'retomar_atendimento', idioma: 'pt_BR', categoria: 'UTILITY', origem: 'Local', quantidadeParametros: 0,
    cabecalho: 'Casa da Baba', corpo: 'Olá! Vimos que sua conversa ficou parada. Se ainda precisar de ajuda, é só responder aqui.' },
]
const DA_META = { nome: 'aviso_oficial', idioma: 'pt_BR', categoria: 'UTILITY', origem: 'Meta', quantidadeParametros: 1, corpo: 'Aviso oficial para {{1}} do pedido.' }
const catalogo = () => q.get('catalogo') === 'vazio' ? [] : q.get('catalogo') === 'misto' ? [...MODELOS, DA_META] : MODELOS

function mensagens() {
  return [
    { id: 'm0', dir: 'in', texto: 'Oi, vocês abrem domingo?', em: new Date(agora - 3 * 86400000).toISOString() },
    { id: 'm1', dir: 'out', texto: 'Abrimos sim, das 11h às 15h.', em: new Date(agora - 3 * 86400000 + 600000).toISOString() },
  ]
}

function CenaConversa() {
  const [, redesenhar] = useState(0)
  const fixa = useRef(mensagens())
  const clienteId = q.get('lead') === '1' ? null : 'cli1'
  const conversa = {
    id: 'c1', nome: 'Renan Okamoto', canal: 'WhatsApp', estado: 'Aberto', conta: 'cliente', clienteId,
    cliente: { tags: [], notas: [], pedidos: 2 }, pedido: null, passagem: null,
    mensagens: fixa.current, ultimaEm: new Date(agora - 3 * 86400000).toISOString(),
  }
  const acoes = useMemo(() => ({
    descarregarRascunho: noop, enviarMidia: noop, definirRascunho: noop, devolverAutomatico: noop, selecionar: noop,
    abrirEncerramento: noop, reabrir: noop, listarAtendentes: async () => [], enviar: noop, prepararOutroModelo: noop,
    listarModelos: async () => {
      ordem.push('listar')
      await espera(120)
      if (q.get('catalogo') === 'erro') throw new Error('Conecte o WhatsApp oficial em Integrações para listar os modelos aprovados.')
      return catalogo()
    },
    enviarModelo: async (id, dados) => {
      ordem.push('modelo:' + id + ':' + JSON.stringify(dados))
      await espera(150)
      if (q.get('envio') === 'erro') throw new Error('O cliente escreveu há mais de 7 dias: a mensagem padrão não pode mais ser enviada. Aguarde ele escrever de novo.')
      fixa.current.push({ id: 'n1', dir: 'out', texto: 'Olá, Renan! Seu pedido #4821 está pronto para retirada na loja.', em: new Date().toISOString(), status: 'Enviada' })
      redesenhar((n) => n + 1)
      return { id: 'n1', status: 'Enviada' }
    },
  }), [])
  const estado = {
    selecionada: conversa, visiveis: [conversa], agora, automaticoPausado: { c1: false },
    rascunho: '', escritaDeFora: 0, fonteApi: true,
  }
  const cat = { janelas: [], canais: CANAIS, respostasProntas: [], cardapio: [], linhas: [], adicionais: [] }
  const acesso = { permite: () => true, acoes: { reabrirConversa: true, editarRespostas: q.get('papel') !== 'operador' }, aoSair: null }
  return (
    <ContextoAcessoModulos value={acesso}><ContextoEstado value={estado}><ContextoAcoes value={acoes}><ContextoCatalogo value={cat}>
      <div style={{ display: 'flex', height: '100dvh' }}>
        <section className={moldura.coluna + ' ' + moldura.centro}>
          <PainelAtendimento aoAbrirNota={noop} aoAbrirGaleria={noop} aoAbrirBiblioteca={() => ordem.push('biblioteca')} />
        </section>
      </div>
    </ContextoCatalogo></ContextoAcoes></ContextoEstado></ContextoAcessoModulos>
  )
}

function CenaGestao() {
  const acoes = useMemo(() => ({ incluirRespostaPronta: noop, editarRespostaPronta: noop, alternarArquivamentoRespostaPronta: noop }), [])
  const estado = { fonteApi: true, selecionada: null, visiveis: [], agora }
  const cat = { janelas: [], canais: CANAIS, respostasProntas: [], cardapio: [], linhas: [], adicionais: [] }
  const acesso = { permite: () => true, acoes: { editarRespostas: true }, aoSair: null }
  return (
    <ContextoAcessoModulos value={acesso}><ContextoEstado value={estado}><ContextoAcoes value={acoes}><ContextoCatalogo value={cat}>
      <main style={{ padding: 16, height: '100dvh', overflow: 'auto', boxSizing: 'border-box' }}><div style={{ maxWidth: 960, margin: '0 auto' }}><AbaRespostas comAutomaticas={false} /></div></main>
    </ContextoCatalogo></ContextoAcoes></ContextoEstado></ContextoAcessoModulos>
  )
}

createRoot(document.getElementById('root')).render(q.get('cena') === 'gestao' ? <CenaGestao /> : <CenaConversa />)
`)

process.env.VITE_FONTE_DADOS = 'api'
const compilado = await build({
  root: raiz, configFile: false, logLevel: 'silent',
  define: { 'process.env.NODE_ENV': '"production"' },
  plugins: [react()],
  build: { write: false, minify: false, cssCodeSplit: false, lib: { entry: '.build/prova-mensagens-padrao/entrada.jsx', formats: ['iife'], name: 'Prova' } },
})
const saidas = (Array.isArray(compilado) ? compilado[0] : compilado).output
const codigo = saidas.find((a) => a.type === 'chunk').code
const estilo = String(saidas.find((a) => a.type === 'asset' && a.fileName.endsWith('.css'))?.source ?? '')
const html = `<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${estilo}</style><div id="root"></div><script>${codigo.replaceAll('</script', '<\\/script')}</script></html>`

const ORIGEM = 'http://prova.local'
const gestaoApi = [
  { id: 'g1', nome: 'pedido_pronto_retirada', idioma: 'pt_BR', categoria: 'UTILITY', origem: 'Local', corpo: 'Olá {{1}}, seu pedido {{2}} está pronto para retirada. Pode vir buscar quando quiser.', rodape: 'Obrigado pela preferência', arquivado: false },
  { id: 'g2', nome: 'promocao_antiga', idioma: 'pt_BR', categoria: 'UTILITY', origem: 'Local', corpo: 'Olá {{1}}, aproveite a promoção da semana.', arquivado: true },
]
const navegador = await chromium.launch({ headless: true })
const erros = []
const TELAS = { desktop: { width: fase === 'antes' ? 1280 : 1440, height: 800 }, celular: { width: 390, height: 844 } }

async function abrir(tela, consulta = '', espera = null) {
  const ctx = await navegador.newContext({ viewport: TELAS[tela], reducedMotion: 'reduce', serviceWorkers: 'block' })
  await ctx.addInitScript(() => {
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' }, usuario: { id: 'u1' } }))
  })
  await ctx.route(ORIGEM + '/**', (rota) => {
    const url = new URL(rota.request().url())
    if (!url.pathname.startsWith('/api/')) return rota.fulfill({ contentType: 'text/html', body: html })
    if (url.pathname === '/api/atendimento/modelos-locais') return rota.fulfill({ json: { success: true, data: gestaoApi } })
    return rota.fulfill({ json: { success: true, data: [] } })
  })
  const pagina = await ctx.newPage()
  pagina.on('pageerror', (e) => erros.push('pageerror: ' + e.message))
  await pagina.goto(`${ORIGEM}/?${consulta}`)
  await (espera ? espera(pagina) : pagina.getByLabel('Mensagem para o cliente', { exact: true })).waitFor({ timeout: 8000 })
  return pagina
}
const foto = (pagina, nome) => pagina.screenshot({ path: resolve(evidencias, `${nome}.png`) })

try {
  if (fase === 'antes') {
    // O caminho de hoje: janela fechada, clique em Respostas, modal de modelo com o erro de integração.
    for (const tela of Object.keys(TELAS)) {
      const pagina = await abrir(tela, 'catalogo=erro')
      await pagina.getByRole('button', { name: 'Respostas', exact: true }).click()
      await pagina.getByRole('dialog').waitFor()
      await pagina.getByRole('alert').filter({ hasText: 'Conecte o WhatsApp oficial' }).waitFor()
      await foto(pagina, `antes-modal-${tela}`)
      console.log('ok    antes-modal-' + tela)
    }
  }
  if (fase === 'depois') {
    const T = { timeout: 8000 }
    const painel = (p) => p.getByRole('group', { name: 'Mensagens padrão do WhatsApp' })
    const abrirPainel = async (p) => { await p.getByRole('button', { name: 'Respostas', exact: true }).click(); await painel(p).waitFor(T) }
    const TEXTO = 'Olá, Renan! Seu pedido #4821 está pronto para retirada na loja.'
    for (const tela of Object.keys(TELAS)) {
      // (a) janela fechada: lista com 3 mensagens locais, sem modal
      let p = await abrir(tela)
      await abrirPainel(p)
      const lista = p.getByRole('list', { name: 'Mensagens padrão' })
      await lista.waitFor(T)
      assert.equal(await lista.getByRole('button').count(), 3)
      assert.equal(await p.getByRole('dialog').count(), 0)
      await foto(p, `depois-a-lista-${tela}`)
      // (b) escolhida: campos e prévia ao vivo
      await lista.getByRole('button').first().click()
      await p.getByLabel('1º campo').fill('Renan')
      await p.getByLabel('2º campo').fill('#4821')
      await painel(p).getByText(TEXTO).waitFor(T)
      await foto(p, `depois-b-escolhida-${tela}`)
      // (c) envio: o painel fecha e a mensagem entra no histórico
      await p.getByRole('button', { name: 'Enviar', exact: true }).click()
      await painel(p).waitFor({ state: 'hidden', ...T })
      await p.getByText(TEXTO).waitFor(T)
      assert.match(await p.evaluate(() => window.ordem.join('|')), /modelo:c1:.*pedido_pronto_retirada.*Renan.*#4821/)
      await foto(p, `depois-c-enviada-${tela}`)
      await p.context().close()
      // (d) lista vazia, com o caminho para a Gestão
      p = await abrir(tela, 'catalogo=vazio')
      await abrirPainel(p)
      await painel(p).locator('[data-vazio]').waitFor(T)
      const link = painel(p).getByRole('link', { name: /Cadastrar em Gestão/ })
      await link.waitFor(T)
      assert.equal(await link.getAttribute('href'), '#/m/atendimento/respostas')
      await foto(p, `depois-d-vazio-${tela}`)
      await p.context().close()
      // (e) erro do servidor no envio, como veio
      p = await abrir(tela, 'envio=erro')
      await abrirPainel(p)
      await p.getByRole('list', { name: 'Mensagens padrão' }).getByRole('button').first().click()
      await p.getByLabel('1º campo').fill('Renan')
      await p.getByLabel('2º campo').fill('#4821')
      await p.getByRole('button', { name: 'Enviar', exact: true }).click()
      await painel(p).getByRole('alert').filter({ hasText: 'O cliente escreveu há mais de 7 dias: a mensagem padrão não pode mais ser enviada. Aguarde ele escrever de novo.' }).waitFor(T)
      await foto(p, `depois-e-erro-${tela}`)
      await p.context().close()
      // (f) Gestão: duas mensagens, uma arquivada
      p = await abrir(tela, 'cena=gestao', (pg) => pg.getByRole('heading', { name: 'Mensagens padrão' }))
      await p.setViewportSize({ width: TELAS[tela].width, height: tela === 'celular' ? 1700 : 1250 })
      const regiao = p.getByRole('region', { name: 'Mensagens padrão' })
      await regiao.scrollIntoViewIfNeeded()
      await regiao.getByLabel('Mostrar arquivadas').check()
      await p.getByText('Arquivada', { exact: true }).waitFor(T)
      assert.equal(await p.locator('section[aria-labelledby="gestao-mensagens-padrao"] ul > li').count(), 2)
      await foto(p, `depois-f-gestao-${tela}`)
      // (g) formulário aberto por uma sugestão pronta, com a prévia
      await p.getByRole('button', { name: 'Nova mensagem padrão' }).click()
      await p.getByRole('form', { name: 'Nova mensagem padrão' }).waitFor(T)
      await p.getByRole('button', { name: 'Pedido saiu para entrega' }).click()
      await p.getByRole('group', { name: /Prévia, com exemplos/ }).getByText(/saiu para entrega/).waitFor(T)
      assert.match(await p.getByLabel('Texto da mensagem').inputValue(), /saiu para entrega/)
      await foto(p, `depois-g-formulario-${tela}`)
      await p.context().close()
      console.log('ok    ' + tela)
    }
  }
  assert.deepEqual(erros, [])
} finally { await navegador.close() }
