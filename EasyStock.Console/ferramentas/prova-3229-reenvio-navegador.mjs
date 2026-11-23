/* eslint-disable no-console */
// Prova da #3229 no navegador: o servidor devolve 200 com a linha Falhou da mesma tentativa (#1599).
//   definitiva (nada saiu): o erro aparece, não há "Conferi" e o reenvio sai com tentativaId NOVA;
//   incerta (pode ter saído): a conferência continua exigida e o Enviar fica travado.
// Ações reais (criarAcoesModelosApi) sobre fetch simulado; componentes reais (PainelMensagensPadrao via Respostas e
// ModalEnviarModelo) com CSS real, em Chromium. Nenhum servidor, nenhum dado real.
//
//   PLAYWRIGHT_MODULE=<pasta do playwright> EVIDENCIAS_3229=<pasta> node ferramentas/prova-3229-reenvio-navegador.mjs
import assert from 'node:assert/strict'
import { mkdirSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'
import { build } from 'vite'
import react from '@vitejs/plugin-react'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const raiz = fileURLToPath(new URL('..', import.meta.url)).replaceAll('\\', '/').replace(/\/$/, '')
const pasta = resolve(raiz, '.build/prova-3229-reenvio')
const evidencias = resolve(process.env.EVIDENCIAS_3229 ?? resolve(tmpdir(), 'prova-3229'))
mkdirSync(pasta, { recursive: true })
mkdirSync(evidencias, { recursive: true })

writeFileSync(resolve(pasta, 'entrada.jsx'), `
import React, { useMemo } from 'react'
import { createRoot } from 'react-dom/client'
import { ContextoAcessoModulos } from '/src/aplicacao/acessoModulos.jsx'
import { ContextoAcoes, ContextoCatalogo, ContextoEstado } from '/src/aplicacao/contextos.js'
import { criarAcoesModelosApi } from '/src/aplicacao/api/modelos.js'
import { PainelAtendimento } from '/src/features/atendimento/PainelAtendimento.jsx'
import { ModalEnviarModelo } from '/src/features/atendimento/ModalEnviarModelo.jsx'
import { CANAIS } from '/src/infra/catalogo.js'
import moldura from '/src/app/moldura.module.css'
import '/src/estilos/base.css'

const q = new URLSearchParams(location.search)
const agora = Date.now()
const noop = () => {}
const conversa = {
  id: 'c1', nome: 'Cliente de Teste', canal: 'WhatsApp', estado: 'Aberto', conta: 'cliente', clienteId: 'cli1',
  cliente: { tags: [], notas: [], pedidos: 0 }, pedido: null, passagem: null, mensagens: [],
  ultimaEm: new Date(agora - 3 * 86400000).toISOString(),
}
function Cena() {
  const acoes = useMemo(() => ({
    descarregarRascunho: noop, enviarMidia: noop, definirRascunho: noop, devolverAutomatico: noop, selecionar: noop,
    abrirEncerramento: noop, reabrir: noop, listarAtendentes: async () => [], enviar: noop,
    ...criarAcoesModelosApi({ despachar: noop }),
  }), [])
  const estado = { selecionada: conversa, visiveis: [conversa], agora, automaticoPausado: { c1: false }, rascunho: '', escritaDeFora: 0, fonteApi: true }
  const cat = { janelas: [], canais: CANAIS, respostasProntas: [], cardapio: [], linhas: [], adicionais: [] }
  const acesso = { permite: () => true, acoes: { reabrirConversa: true, editarRespostas: true }, aoSair: null }
  return (
    <ContextoAcessoModulos value={acesso}><ContextoEstado value={estado}><ContextoAcoes value={acoes}><ContextoCatalogo value={cat}>
      {q.get('cena') === 'modal'
        ? <ModalEnviarModelo conversa={conversa} aoFechar={noop} />
        : <div style={{ display: 'flex', height: '100dvh' }}><section className={moldura.coluna + ' ' + moldura.centro}>
            <PainelAtendimento aoAbrirNota={noop} aoAbrirGaleria={noop} aoAbrirBiblioteca={noop} />
          </section></div>}
    </ContextoCatalogo></ContextoAcoes></ContextoEstado></ContextoAcessoModulos>
  )
}
createRoot(document.getElementById('root')).render(<Cena />)
`)

process.env.VITE_FONTE_DADOS = 'api'
const compilado = await build({
  root: raiz, configFile: false, logLevel: 'silent',
  define: { 'process.env.NODE_ENV': '"production"' },
  plugins: [react()],
  build: { write: false, minify: false, cssCodeSplit: false, lib: { entry: '.build/prova-3229-reenvio/entrada.jsx', formats: ['iife'], name: 'Prova' } },
})
const saidas = (Array.isArray(compilado) ? compilado[0] : compilado).output
const codigo = saidas.find((a) => a.type === 'chunk').code
const estilo = String(saidas.find((a) => a.type === 'asset' && a.fileName.endsWith('.css'))?.source ?? '')
const html = `<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${estilo}</style><div id="root"></div><script>${codigo.replaceAll('</script', '<\\/script')}</script></html>`

// Frases reais do servidor (EnviarModeloConversaUseCase e Mensagem.RegistrarFalhaEnvio).
const FRASES = {
  incerta: 'Envio incerto (sem resposta do canal): Confira a conversa no WhatsApp antes de iniciar outro envio.',
  permanente: 'O WhatsApp não confirmou o envio deste modelo. Confira a conexão e o modelo antes de tentar novamente.',
}
const MODELO = { nome: 'retomar_atendimento', idioma: 'pt_BR', categoria: 'UTILITY', origem: 'Meta', quantidadeParametros: 0,
  corpo: 'Olá! Vimos que sua conversa ficou parada. Se ainda precisar de ajuda, é só responder aqui.' }
const ORIGEM = 'http://localhost:4399' // contexto seguro: crypto.randomUUID existe só nele
const navegador = await chromium.launch({ headless: true })
const erros = []

async function abrir(cena, tipo) {
  const ctx = await navegador.newContext({ viewport: { width: 1280, height: 800 }, reducedMotion: 'reduce', serviceWorkers: 'block' })
  await ctx.addInitScript(() => {
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' }, usuario: { id: 'u1' } }))
  })
  const tentativas = []
  await ctx.route(ORIGEM + '/**', (rota) => {
    const url = new URL(rota.request().url())
    if (!url.pathname.startsWith('/api/')) return rota.fulfill({ contentType: 'text/html', body: html })
    if (url.pathname === '/api/atendimento/modelos') return rota.fulfill({ json: { success: true, data: [MODELO] } })
    if (url.pathname === '/api/atendimento/conversas/c1/mensagens/modelo') {
      const corpo = rota.request().postDataJSON()
      tentativas.push(corpo.tentativaId)
      return rota.fulfill({ json: { success: true, data: {
        id: 'm-falha', direcao: 'Saida', autor: 'Dona', tipoConteudo: 'Modelo', status: 'Falhou', erro: FRASES[tipo],
        texto: MODELO.corpo, modelo: { nome: MODELO.nome, idioma: MODELO.idioma, parametros: [] }, enviadaEm: new Date().toISOString(),
      } } })
    }
    return rota.fulfill({ json: { success: true, data: [] } })
  })
  const pagina = await ctx.newPage()
  pagina.on('pageerror', (e) => erros.push('pageerror: ' + e.message)); pagina.on('console', (m) => process.env.DEPURAR && console.log('console:', m.text()))
  await pagina.goto(`${ORIGEM}/?cena=${cena}`)
  return { pagina, tentativas }
}
const foto = (pagina, nome) => pagina.screenshot({ path: resolve(evidencias, `${nome}.png`) })
const T = { timeout: 8000 }
const CONFERI = /Conferi no WhatsApp/

try {
  for (const tipo of ['permanente', 'incerta']) {
    // Painel de mensagens padrão (botão Respostas do compositor)
    let { pagina: p, tentativas } = await abrir('painel', tipo)
    await p.getByLabel('Mensagem para o cliente', { exact: true }).waitFor(T)
    await p.getByRole('button', { name: 'Respostas', exact: true }).click()
    await p.getByRole('list', { name: 'Mensagens padrão' }).getByRole('button').first().click()
    const enviar = p.getByRole('button', { name: 'Enviar', exact: true })
    await enviar.click()
    await p.getByRole('alert').filter({ hasText: FRASES[tipo] }).waitFor(T).catch(async (e) => { console.log(await p.locator('body').innerText()); throw e })
    if (tipo === 'permanente') {
      assert.equal(await p.getByText(CONFERI).count(), 0, 'definitiva não pede conferência (painel)')
      assert.equal(await enviar.isDisabled(), false, 'definitiva deixa reenviar (painel)')
      await foto(p, 'painel-definitiva')
      await enviar.click()
      await p.waitForTimeout(300)
      assert.equal(tentativas.length, 2)
      assert.notEqual(tentativas[0], tentativas[1], 'reenvio leva tentativaId nova (painel)')
    } else {
      await p.getByText(CONFERI).waitFor(T)
      assert.equal(await enviar.isDisabled(), true, 'incerta trava o Enviar (painel)')
      await foto(p, 'painel-incerta')
      assert.equal(tentativas.length, 1)
    }
    console.log('ok painel ' + tipo)
    await p.context().close()

    // Modal de envio (ModalEnviarModelo)
    ;({ pagina: p, tentativas } = await abrir('modal', tipo))
    await p.locator('select').selectOption({ index: 1 }, T).catch(async (e) => { console.log(await p.locator('body').innerText()); throw e })
    const botao = p.getByRole('button', { name: 'Enviar modelo', exact: true })
    await botao.click()
    await p.getByRole('alert').filter({ hasText: FRASES[tipo] }).waitFor(T).catch(async (e) => { console.log(await p.locator('body').innerText()); throw e })
    if (tipo === 'permanente') {
      assert.equal(await p.getByText(CONFERI).count(), 0, 'definitiva não pede conferência (modal)')
      assert.equal(await botao.isDisabled(), false, 'definitiva deixa reenviar (modal)')
      await foto(p, 'modal-definitiva')
      await botao.click()
      await p.waitForTimeout(300)
      assert.equal(tentativas.length, 2)
      assert.notEqual(tentativas[0], tentativas[1], 'reenvio leva tentativaId nova (modal)')
    } else {
      await p.getByText(CONFERI).waitFor(T)
      assert.equal(await botao.isDisabled(), true, 'incerta trava o Enviar modelo (modal)')
      await foto(p, 'modal-incerta')
      assert.equal(tentativas.length, 1)
    }
    console.log('ok modal ' + tipo)
    await p.context().close()
  }
  assert.deepEqual(erros, [])
  console.log('prints em ' + evidencias)
} finally {
  await navegador.close()
}
