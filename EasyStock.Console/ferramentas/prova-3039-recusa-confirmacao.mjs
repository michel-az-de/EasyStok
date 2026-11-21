// #3039: recusar o modal de conversa de outra (Cancelar, Esc) não deixa a frase do 409 ("Confirme para assumir") na
// faixa nem no balão: a faixa fica limpa ao assumir e o balão diz que a conversa continua com a outra atendente.
// A camada de API rejeita com CONFIRMACAO_RECUSADA; sem anfitrião o erro original (CONVERSA_DE_OUTRA) segue.
// Texto real do backend (ConversaAtribuidaOutraException). App real no Chromium, HTTP controlado.
// Sem CONSOLE_TEST_URL (fora do `npm run provas`) sobe o próprio Vite em modo API na porta 5894.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { resolve } from 'node:path'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const raiz = resolve(import.meta.dirname, '..')

let servidor = null
let origem = process.env.CONSOLE_TEST_URL
if (!origem) {
  process.env.VITE_FONTE_DADOS = 'api'
  const { createServer } = await import('vite')
  servidor = await createServer({ root: raiz, logLevel: 'error', server: { host: '127.0.0.1', port: 5894, strictPort: true } })
  await servidor.listen()
  origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
}

const MENSAGEM = 'Conversa com Bia. Confirme para assumir.'
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

const dialogo = (page) => page.getByRole('dialog', { name: 'Conversa de outra atendente' })
const casos = {
  assumir: { situacao: 'Automatica', abrir: (p) => p.getByRole('button', { name: 'Assumir conversa' }), disparar: (b) => b.click(), caminho: '/assumir' },
  responder: { situacao: 'Assumida', abrir: (p) => p.getByRole('textbox', { name: 'Mensagem para o cliente' }), disparar: async (c) => { await c.fill('Posso ajudar?'); await c.press('Enter') }, caminho: '/mensagens' },
}

try {
  for (const [nome, caso] of Object.entries(casos)) {
    for (const saida of ['cancelar', 'esc']) {
      const { contexto, page, chamadas, nativos } = await preparar(caso.situacao)
      const origemDoFoco = caso.abrir(page)
      await origemDoFoco.waitFor()
      await caso.disparar(origemDoFoco)
      const modal = dialogo(page)
      await modal.waitFor()
      if (saida === 'cancelar') await modal.getByRole('button', { name: 'Cancelar' }).click()
      else await page.keyboard.press('Escape')
      await modal.waitFor({ state: 'detached' })
      await page.waitForTimeout(700)
      assert.deepEqual(nativos, [], `${nome}/${saida}: nenhum diálogo nativo`)
      assert.equal(chamadas.length, 1, `${nome}/${saida}: recusar não repete a chamada`)
      const corpo = await page.locator('body').innerText()
      assert.doesNotMatch(corpo, /Confirme/, `${nome}/${saida}: nenhuma frase pedindo confirmação fica na tela`)
      if (nome === 'assumir') {
        assert.equal(await page.locator('output').filter({ hasText: /Confirme/ }).count(), 0, `${nome}/${saida}: a faixa não pede confirmação`)
        assert.equal(await origemDoFoco.isEnabled(), true, `${nome}/${saida}: o botão fica habilitado`)
        assert.equal(await origemDoFoco.evaluate((el) => el === document.activeElement), true, `${nome}/${saida}: o foco volta ao controle de origem`)
      } else {
        assert.match(corpo, /continua com a outra atendente/, `${nome}/${saida}: o balão diz que a conversa continua com a outra`)
      }
      await contexto.close()
    }
  }
  // Camada de API: recusa rejeita com CONFIRMACAO_RECUSADA (status e cause do original); sem anfitrião fica CONVERSA_DE_OUTRA.
  {
    const { contexto, page, chamadas } = await preparar('Automatica')
    await page.evaluate(() => { location.hash = '#/m/inicio' })
    await dialogo(page).waitFor({ state: 'detached' }).catch(() => {})
    const r = await page.evaluate(async () => {
      const api = await import('/src/infra/api/conversasApi.js')
      const ler = (e) => ({ codigo: e?.codigo, status: e?.status, causa: e?.cause?.codigo, msg: e?.message })
      const semAnfitriao = await api.assumir('conv-bia').then(() => null, ler)
      const solta = api.definirPerguntaDeOutra(async () => false)
      const recusa = await api.assumir('conv-bia').then(() => null, ler)
      solta()
      return { semAnfitriao, recusa }
    })
    assert.equal(r.semAnfitriao?.codigo, 'CONVERSA_DE_OUTRA', 'sem anfitrião o erro original segue')
    assert.equal(r.recusa?.codigo, 'CONFIRMACAO_RECUSADA', 'recusa rejeita com CONFIRMACAO_RECUSADA')
    assert.equal(r.recusa?.status, 409, 'a recusa mantém o status')
    assert.equal(r.recusa?.causa, 'CONVERSA_DE_OUTRA', 'a recusa guarda o erro original em cause')
    assert.doesNotMatch(r.recusa?.msg ?? '', /Confirme/, 'a frase da recusa não pede confirmação')
    assert.equal(chamadas.length, 2, 'uma tentativa sem anfitrião e uma com recusa, nenhuma repetida')
    await contexto.close()
  }
  assert.deepEqual(erros, [], `erros na página: ${erros.join(' | ')}`)
  console.log('ok prova-3039: recusar a conversa de outra não pede confirmação na faixa nem no balão')
} finally {
  await navegador.close()
  await servidor?.close()
}
