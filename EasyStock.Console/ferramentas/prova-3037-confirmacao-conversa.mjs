// #3037 (fecha o padrão da #3011): o 409 CONVERSA_DE_OUTRA (assumir e responder) pergunta num modal (role=dialog com a
// mensagem da API), nunca no window.confirm nativo. Cancelar e Esc não repetem a chamada; Confirmar repete uma vez
// com `confirmar` e a MESMA Idempotency-Key; o foco volta ao controle de origem. App real no Chromium, HTTP controlado.
// Sem CONSOLE_TEST_URL (fora do `npm run provas`) sobe o próprio Vite em modo API na porta 5894.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, resolve } from 'node:path'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const raiz = resolve(import.meta.dirname, '..')

// 1. A camada de API não pergunta pelo navegador: nenhum window.confirm sobra em src.
const fontes = (dir) => readdirSync(dir).flatMap((n) => {
  const c = join(dir, n)
  return statSync(c).isDirectory() ? fontes(c) : /\.(jsx?|mjs)$/.test(c) ? [c] : []
})
let servidor = null
let origem = process.env.CONSOLE_TEST_URL
if (!origem) {
  process.env.VITE_FONTE_DADOS = 'api'
  const { createServer } = await import('vite')
  servidor = await createServer({ root: raiz, logLevel: 'error', server: { host: '127.0.0.1', port: 5894, strictPort: true } })
  await servidor.listen()
  origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
}

const MENSAGEM = 'Conversa com Bia, atendida por Carla. Confirme para continuar.'
const conversa = (situacao) => ({
  id: 'conv-bia', contatoNome: 'Bia Souza', contatoIdExterno: '5511999990001', clienteId: null, canal: 'WhatsApp', situacao,
  assumidaPorUsuarioId: situacao === 'Assumida' ? 'tatiana' : null, assumidaPorNome: situacao === 'Assumida' ? 'Atendente de teste' : null,
  ultimaMensagemEm: new Date().toISOString(), naoLidas: 0, ultimaMensagemTexto: 'Oi', dentroDaJanela: true,
})

const navegador = await chromium.launch({ headless: true })
if (servidor) { // aquece o Vite próprio: a primeira página pré-empacota as dependências e recarrega
  const aquecer = await navegador.newPage()
  await aquecer.goto(origem, { waitUntil: 'networkidle', timeout: 120_000 }).catch(() => {})
  await aquecer.waitForTimeout(1500)
  await aquecer.reload({ waitUntil: 'networkidle', timeout: 120_000 }).catch(() => {})
  await aquecer.close()
}
const erros = []

async function preparar(situacao) {
  const chamadas = []
  const nativos = []
  const contexto = await navegador.newContext({ viewport: { width: 1440, height: 900 }, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja da prova' }, usuario: { id: 'tatiana', nome: 'Atendente de teste' } })))
  await contexto.route('**/api/**', async (route) => {
    const req = route.request(), url = new URL(req.url()), path = url.pathname
    if (!path.startsWith('/api/')) return route.continue()
    const ok = (data, extra = {}) => route.fulfill({ json: { data, ...extra } })
    const escrita = path === '/api/atendimento/conversas/conv-bia/assumir' || path === '/api/atendimento/conversas/conv-bia/mensagens'
    if (req.method() === 'POST' && escrita) {
      const corpo = JSON.parse(req.postData() || '{}')
      const confirmou = url.searchParams.get('confirmar') === 'true' || corpo.confirmar === true
      chamadas.push({ path, confirmou, chave: req.headers()['idempotency-key'] ?? null })
      if (!confirmou) return route.fulfill({ status: 409, json: { error: { code: 'CONVERSA_DE_OUTRA', message: MENSAGEM } } })
      if (path.endsWith('/assumir')) return ok({ ...conversa('Assumida') })
      return ok({ id: 'm-novo', conversaId: 'conv-bia', direcao: 'Saida', tipo: 'Texto', texto: 'Posso ajudar?', status: 'Enviada', criadaEm: new Date().toISOString() })
    }
    if (req.method() === 'POST' && path.endsWith('/marcar-lida')) return ok(null)
    if (path === '/api/auth/me/modulos') return ok({ modulos: [{ id: 'atendimento', liberado: true }], acoes: { atenderConversas: true } })
    if (path === '/api/atendimento/conversas') return ok([conversa(situacao)])
    if (path.endsWith('/mensagens')) return ok([])
    if (path.endsWith('/pedido')) return ok(null)
    if (path.endsWith('/dossie')) return ok({ cliente: null, enderecos: [], totalPedidos: 0, notas: [], tags: [] })
    if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
    if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
    if (path === '/api/notificacoes/badge') return ok({ count: 0 })
    return ok([])
  })
  const page = await contexto.newPage()
  page.setDefaultTimeout(30000)
  page.on('pageerror', (e) => erros.push(e.message))
  page.on('dialog', async (d) => { nativos.push(d.message()); await d.dismiss() })
  await page.goto(`${origem}/#/m/atendimento/balcao`, { waitUntil: 'networkidle', timeout: 120_000 })
  await page.getByRole('button', { name: /^Todas/ }).click()
  await page.getByText('Bia Souza').first().click()
  return { contexto, page, chamadas, nativos }
}

// Espera o modal OU o diálogo nativo (no master, o nativo vem primeiro e é dispensado).
async function perguntaAberta(page, nativos) {
  const modal = page.getByRole('dialog', { name: 'Conversa de outra atendente' })
  await Promise.race([modal.waitFor().catch(() => {}), page.waitForEvent('dialog').catch(() => {})])
  assert.deepEqual(nativos, [], `window.confirm nativo disparou em vez do modal: ${nativos.join(' | ')}`)
  assert.equal(await modal.count(), 1, 'o modal de confirmação não apareceu')
  assert.match(await modal.innerText(), new RegExp(MENSAGEM.slice(0, 20)), 'o modal mostra a mensagem da API')
  return modal
}

const casos = {
  assumir: {
    situacao: 'Automatica',
    abrir: (page) => page.getByRole('button', { name: 'Assumir conversa' }),
    disparar: (botao) => botao.click(),
    caminho: '/assumir',
  },
  responder: {
    situacao: 'Assumida',
    abrir: (page) => page.getByRole('textbox', { name: 'Mensagem para o cliente' }),
    disparar: async (campo) => { await campo.fill('Posso ajudar?'); await campo.press('Enter') },
    caminho: '/mensagens',
  },
}

try {
  for (const [nome, caso] of Object.entries(casos)) {
    // Cancelar, Esc e Confirmar, cada um numa conversa nova.
    for (const saida of ['cancelar', 'esc', 'confirmar']) {
      const { contexto, page, chamadas, nativos } = await preparar(caso.situacao)
      const origemDoFoco = caso.abrir(page)
      await origemDoFoco.waitFor()
      await caso.disparar(origemDoFoco)
      const modal = await perguntaAberta(page, nativos)
      assert.equal(chamadas.length, 1, `${nome}/${saida}: uma tentativa antes da pergunta`)
      assert.equal(chamadas[0].confirmou, false, `${nome}/${saida}: a primeira tentativa vai sem confirmar`)
      assert.ok(chamadas[0].path.endsWith(caso.caminho), `${nome}/${saida}: chamou ${chamadas[0].path}`)
      if (saida === 'cancelar') await modal.getByRole('button', { name: 'Cancelar' }).click()
      if (saida === 'esc') await page.keyboard.press('Escape')
      if (saida === 'confirmar') await modal.getByRole('button', { name: 'Confirmar' }).click()
      await modal.waitFor({ state: 'detached' })
      await page.waitForTimeout(700)
      assert.deepEqual(nativos, [], `${nome}/${saida}: nenhum diálogo nativo`)
      if (saida === 'confirmar') {
        assert.equal(chamadas.length, 2, `${nome}: repete uma vez`)
        assert.equal(chamadas[1].confirmou, true, `${nome}: a repetição leva confirmar=true`)
        if (nome === 'responder') assert.ok(chamadas[1].chave, `${nome}: tem Idempotency-Key`) // o Assumir não leva chave
        assert.equal(chamadas[1].chave, chamadas[0].chave, `${nome}: a MESMA chave de idempotência`)
      } else {
        assert.equal(chamadas.length, 1, `${nome}/${saida}: recusar não repete a chamada`)
        assert.equal(await origemDoFoco.evaluate((el) => el === document.activeElement), true, `${nome}/${saida}: o foco volta ao controle de origem`)
      }
      await contexto.close()
    }
  }
  // Desmontar a tela com a pergunta aberta recusa: a ação rejeita com o erro original, não fica pendurada.
  {
    const { contexto, page, chamadas, nativos } = await preparar('Automatica')
    await page.evaluate(async () => {
      const api = await import('/src/infra/api/conversasApi.js')
      window.__acao = api.assumir('conv-bia').then(() => 'resolveu', (e) => `rejeitou:${e?.codigo}`)
    })
    await perguntaAberta(page, nativos)
    await page.evaluate(() => { location.hash = '#/m/inicio' })
    await page.getByRole('dialog', { name: 'Conversa de outra atendente' }).waitFor({ state: 'detached' })
    const fim = await Promise.race([
      page.evaluate(() => window.__acao),
      new Promise((r) => setTimeout(() => r('pendurada'), 5000)),
    ])
    assert.equal(fim, 'rejeitou:CONVERSA_DE_OUTRA', 'desmontado com pergunta pendente: a ação rejeita com o erro original')
    assert.equal(chamadas.length, 1, 'desmontar não repete a chamada')
    await contexto.close()
  }
  // A camada de API não pergunta pelo navegador: nenhum window.confirm sobra em src.
  const comConfirmNativo = fontes(join(raiz, 'src')).filter((c) => /window\.confirm\(/.test(readFileSync(c, 'utf8')))
  assert.deepEqual(comConfirmNativo, [], `window.confirm em src: ${comConfirmNativo.map((c) => c.slice(raiz.length + 1)).join(', ')}`)
  assert.deepEqual(erros, [], `erros na página: ${erros.join(' | ')}`)
  console.log('ok prova-3037: 409 CONVERSA_DE_OUTRA pergunta no modal (assumir e responder), sem window.confirm')
} finally {
  await navegador.close()
  await servidor?.close()
}
