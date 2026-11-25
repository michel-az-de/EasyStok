// #3363 (S1 e S2 de docs/plan/atendimento-fila-teclado): a fila do Balcão pelo teclado, medida no Chromium em 1440x900.
// S1 a lista de cartões é uma parada de Tab (foco itinerante: o cartão aberto, o último com foco ou o primeiro). ↑ ↓, j k,
//    Home e End movem o foco entre os cartões, só com o foco num cartão e sem Ctrl, Alt, Meta ou Shift. j e k no campo
//    de mensagem continuam escrevendo. Enter no cartão abre a conversa e leva o foco ao campo (regressão).
// S2 Esc no campo com texto devolve o foco ao cartão da conversa aberta, sem apagar o rascunho; com a barra de respostas
//    aberta o Esc só fecha a barra (o segundo Esc volta ao cartão).
// S3 Ctrl+Enter (ou Meta+Enter) no campo envia pelo mesmo caminho do Enter e, depois do envio aceito, abre a conversa do topo
//    de "Precisa de você" que não é a atual, com o foco no campo; texto acima do limite, Shift ou Alt não enviam nem trocam;
//    com o modal "Enviar e assumir?" o Ctrl+Enter abre o modal e o Enter que confirma envia e avança; sem outra conversa na
//    fila fica onde está. Enter sozinho só envia. A regra do topo é pura (`proximaDaFila`, em Node: S3f).
// Modo API com a rede simulada (a medição de partida está em medicao-modo-api.md: 56 paradas de Tab até o campo com 35
// cartões) e modo demonstração com a aba "Precisa de você" (11 cartões, 33 paradas no Red; 35 na spec). CONSOLE_TEST_URL e
// CONSOLE_DEMO_URL apontam para Vites já no ar (o rodar-provas injeta a primeira); sem elas a prova sobe os seus.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { resolve } from 'node:path'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pastaConsole = resolve(import.meta.dirname, '..')
const servidores = []
async function subir(fonteDeDados) {
  if (fonteDeDados) process.env.VITE_FONTE_DADOS = fonteDeDados
  else delete process.env.VITE_FONTE_DADOS
  const { createServer } = await import('vite')
  const servidor = await createServer({ root: pastaConsole, logLevel: 'silent', server: { host: '127.0.0.1', port: 0, strictPort: false } })
  await servidor.listen()
  servidores.push(servidor)
  return servidor.resolvedUrls.local[0].replace(/\/$/, '')
}
const origemDemo = process.env.CONSOLE_DEMO_URL ?? await subir(null)
const origemApi = process.env.CONSOLE_TEST_URL ?? await subir('api')

const falhas = []
const confere = async (descricao, fn) => {
  try { await fn(); console.log('ok ' + descricao) } catch (erro) {
    falhas.push(descricao); console.log(`FALHA ${descricao}\n ${String(erro.message).split('\n').slice(0, 4).join(' ')}`)
  }
}

const navegador = await chromium.launch({ headless: true })
const CARTOES = '[role=list] button[aria-current]'
const FRASE = 'Oi, tudo bem?'
const campoDe = (pagina) => pagina.getByLabel('Mensagem para o cliente')
// O fio da conversa aberta: o texto só está nele se foi enviado nela.
const textoNoFio = (pagina, texto) => pagina.locator('[role=log]', { hasText: texto }).count()
const idAberto = (pagina) => pagina.evaluate(() => document.querySelector('[role=list] button[aria-current="true"][data-conversa-id]')?.dataset.conversaId ?? null)
const dialogoDoModal = (pagina) => pagina.getByRole('dialog', { name: 'Enviar e assumir esta conversa?' })
async function digitarEPressionar(pagina, texto, combinacao) {
  const campo = campoDe(pagina)
  await campo.click()
  await campo.fill(texto)
  await pagina.keyboard.press(combinacao)
  await pagina.waitForTimeout(700)
}
const ehOCampo = () => document.activeElement?.tagName === 'TEXTAREA' && document.activeElement.labels?.[0]?.textContent === 'Mensagem para o cliente'
const indiceDoFoco = (pagina) => pagina.evaluate((seletor) => [...document.querySelectorAll(seletor)].indexOf(document.activeElement), CARTOES)
const focoNoCartaoAberto = (pagina) => pagina.evaluate(() => {
  const a = document.activeElement
  return a?.tagName === 'BUTTON' && a.getAttribute('aria-current') === 'true' && Boolean(a.closest('[role=list]'))
})

async function contextoNovo() {
  return navegador.newContext({ viewport: { width: 1440, height: 900 }, locale: 'pt-BR', colorScheme: 'light', reducedMotion: 'reduce' })
}

// 35 conversas assumidas pela própria atendente, como na medição: a rede simulada responde só o que o Balcão pede.
async function abrirApi({ quantas = 35, naoLidas = 11, todas = true, abrir = 'Pessoa 05', naFilaDaEquipe = false, atrasoAssumirMs = 0, primeiraAutomatica = false } = {}) {
  const contexto = await contextoNovo()
  await contexto.addInitScript(() => {
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja da fila' }, usuario: { id: 'dona', nome: 'Dona' } }))
    sessionStorage.setItem('easystok.confirmouEnviarAssumindo.v1:empresa:dona', '1')
  })
  const agora = Date.now()
  const conversas = Array.from({ length: quantas }, (_, i) => ({ id: `c${i}`, contatoNome: `Pessoa ${String(i).padStart(2, '0')}`, contatoIdExterno: `551199999${String(i).padStart(4, '0')}`, canal: 'WhatsApp', situacao: i === 0 && primeiraAutomatica ? 'Automatica' : 'Assumida', assumidaPorUsuarioId: naFilaDaEquipe ? null : 'dona', ultimaMensagemEm: new Date(agora - i * 60000).toISOString(), naoLidas: i < naoLidas ? 1 : 0 }))
  await contexto.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    if (!path.startsWith('/api/')) return route.continue()
    const metodo = route.request().method()
    const ok = (data) => route.fulfill({ json: { data } })
    if (atrasoAssumirMs && path.endsWith('/assumir')) await new Promise((resolver) => setTimeout(resolver, atrasoAssumirMs))
    if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros'].map((id) => ({ id, liberado: true })), acoes: {} })
    if (path === '/api/atendimento/conversas') return ok(conversas)
    if (path.endsWith('/mensagens') && metodo === 'GET') return ok([{ id: `${path}-m`, direcao: 'Entrada', texto: 'Bom dia, tudo bem?', enviadaEm: new Date(agora - 120000).toISOString(), tipoConteudo: 'Texto' }])
    if (path.endsWith('/pedido')) return ok(null)
    if (path.endsWith('/dossie')) return ok({ cliente: null, enderecos: [], totalPedidos: 0, notas: [], tags: [] })
    if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
    if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
    if (path === '/api/atendimento/comanda/cardapio') return ok({ itens: [], porcoes: {} })
    return ok([])
  })
  const pagina = await contexto.newPage()
  pagina.setDefaultTimeout(15_000)
  await pagina.goto(`${origemApi}/#/m/atendimento/balcao`, { waitUntil: 'networkidle', timeout: 120_000 })
  if (todas) await pagina.getByRole('button', { name: /^Todas/ }).click()
  await pagina.getByText(abrir, { exact: true }).first().click()
  await pagina.getByLabel('Mensagem para o cliente').waitFor()
  await pagina.waitForTimeout(500)
  return { contexto, pagina, campo: pagina.getByLabel('Mensagem para o cliente') }
}

// Demonstração, aba "Precisa de você": abre a 3ª conversa (a mesma que a prova-3330 usa).
// Na demonstração não há sessão, então a chave do "Enviar e assumir?" é nula e o modal volta a cada envio. Com `comSessao`
// e `confirmado` o envio assume direto (a sessão já confirmou); com `comSessao` sozinho é a 1ª vez da sessão.
async function abrirDemo({ comSessao = false, confirmado = false } = {}) {
  const contexto = await contextoNovo()
  if (comSessao) {
    await contexto.addInitScript((marcar) => {
      sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja da fila' }, usuario: { id: 'dona', nome: 'Dona' } }))
      if (marcar) sessionStorage.setItem('easystok.confirmouEnviarAssumindo.v1:empresa:dona', '1')
    }, confirmado)
  }
  const pagina = await contexto.newPage()
  pagina.setDefaultTimeout(15_000)
  await pagina.goto(`${origemDemo}/#/m/atendimento/balcao`, { waitUntil: 'networkidle', timeout: 120_000 })
  await pagina.locator(CARTOES).first().waitFor()
  await pagina.locator(CARTOES).nth(2).click()
  await pagina.getByLabel('Mensagem para o cliente').waitFor()
  await pagina.waitForTimeout(500)
  return { contexto, pagina }
}

// Paradas de Tab do início do documento até o campo da mensagem (o método da medição).
async function paradasDeTabAteOCampo(pagina) {
  await pagina.evaluate(() => { document.activeElement?.blur(); window.scrollTo(0, 0) })
  await pagina.mouse.click(1, 1)
  await pagina.evaluate(() => document.activeElement?.blur())
  for (let n = 1; n <= 80; n += 1) {
    await pagina.keyboard.press('Tab')
    if (await pagina.evaluate(ehOCampo)) return n
  }
  return Infinity
}

async function andarPelaLista(pagina) {
  const cartoes = pagina.locator(CARTOES)
  const total = await cartoes.count()
  assert.ok(total > 8, `lista com ${total} cartões`)
  const tecla = async (nome, esperado) => {
    await pagina.keyboard.press(nome)
    await pagina.waitForTimeout(60)
    assert.equal(await indiceDoFoco(pagina), esperado, `${nome} deixou o foco no cartão errado`)
  }
  await cartoes.nth(3).focus()
  assert.equal(await indiceDoFoco(pagina), 3, 'o foco não ficou no cartão 3')
  await tecla('ArrowDown', 4)
  await tecla('j', 5)
  await tecla('ArrowUp', 4)
  await tecla('k', 3)
  // Com modificador a tecla é do navegador e do leitor de tela: o foco não anda.
  for (const combinacao of ['Control+ArrowDown', 'Alt+j', 'Meta+j', 'Shift+ArrowDown']) await tecla(combinacao, 3)
  await tecla('End', total - 1)
  await tecla('Home', 0)
  await tecla('ArrowUp', 0)
}

const abrirBarraDeRespostas = async (campo, pagina) => {
  await campo.click()
  await pagina.keyboard.type('/')
  const botao = pagina.locator('button[aria-haspopup="listbox"]')
  await pagina.waitForTimeout(300)
  assert.equal(await botao.getAttribute('aria-expanded'), 'true', 'a barra de respostas não abriu com "/"')
  return botao
}

try {
  await confere('S1a (API, Todas, 35 cartões): até 24 paradas de Tab do início ao campo', async () => {
    const { contexto, pagina } = await abrirApi()
    try {
      const paradas = await paradasDeTabAteOCampo(pagina)
      console.log(`  paradas de Tab até o campo (API): ${paradas}`)
      assert.ok(paradas <= 24, `${paradas} paradas de Tab até o campo (meta: 24 ou menos)`)
    } finally { await contexto.close() }
  })

  await confere('S1b (API): setas, j k, Home e End movem o foco entre cartões; com Ctrl, Alt, Meta ou Shift não', async () => {
    const { contexto, pagina } = await abrirApi()
    try { await andarPelaLista(pagina) } finally { await contexto.close() }
  })

  await confere('S1c (API): j e k no campo de mensagem escrevem e o foco fica nele', async () => {
    const { contexto, pagina, campo } = await abrirApi()
    try {
      await campo.focus()
      await pagina.keyboard.type('jk')
      assert.equal(await campo.inputValue(), 'jk')
      assert.ok(await pagina.evaluate(ehOCampo), 'o foco saiu do campo')
    } finally { await contexto.close() }
  })

  await confere('S1d (API): Enter no cartão focado abre a conversa e o foco cai no campo', async () => {
    const { contexto, pagina } = await abrirApi()
    try {
      const cartoes = pagina.locator(CARTOES)
      await cartoes.nth(7).focus()
      await pagina.keyboard.press('Enter')
      await pagina.waitForTimeout(500)
      assert.equal(await cartoes.nth(7).getAttribute('aria-current'), 'true', 'a conversa 7 não abriu')
      assert.ok(await pagina.evaluate(ehOCampo), 'o foco não caiu no campo')
    } finally { await contexto.close() }
  })

  await confere('S2a (API): Esc no campo com texto leva o foco ao cartão aberto e o texto fica', async () => {
    const { contexto, pagina, campo } = await abrirApi()
    try {
      await campo.click()
      await campo.fill('teste de atalho')
      await pagina.keyboard.press('Escape')
      await pagina.waitForTimeout(100)
      assert.ok(await focoNoCartaoAberto(pagina), 'o foco não foi ao cartão da conversa aberta')
      assert.equal(await campo.inputValue(), 'teste de atalho', 'o rascunho mudou')
    } finally { await contexto.close() }
  })

  await confere('S2b (API): com a barra de respostas aberta, Esc só fecha a barra e o foco fica no campo', async () => {
    const { contexto, pagina, campo } = await abrirApi()
    try {
      const botao = await abrirBarraDeRespostas(campo, pagina)
      await pagina.keyboard.press('Escape')
      await pagina.waitForTimeout(100)
      assert.equal(await botao.getAttribute('aria-expanded'), 'false', 'a barra não fechou')
      assert.ok(await pagina.evaluate(ehOCampo), 'o foco saiu do campo')
    } finally { await contexto.close() }
  })

  await confere('S2c (API): o segundo Esc, com a barra já fechada, volta ao cartão aberto', async () => {
    const { contexto, pagina, campo } = await abrirApi()
    try {
      await abrirBarraDeRespostas(campo, pagina)
      await pagina.keyboard.press('Escape')
      await pagina.waitForTimeout(100)
      await pagina.keyboard.press('Escape')
      await pagina.waitForTimeout(100)
      assert.ok(await focoNoCartaoAberto(pagina), 'o foco não foi ao cartão da conversa aberta')
    } finally { await contexto.close() }
  })

  await confere('S1a e S1b (demonstração, Precisa de você, 11 cartões): até 25 paradas de Tab e o foco anda pelos cartões', async () => {
    const { contexto, pagina } = await abrirDemo()
    try {
      const paradas = await paradasDeTabAteOCampo(pagina)
      console.log(`  paradas de Tab até o campo (demonstração): ${paradas}`)
      assert.ok(paradas <= 25, `${paradas} paradas de Tab até o campo (meta: 25 ou menos)`)
      await andarPelaLista(pagina)
    } finally { await contexto.close() }
  })
  // --- S3: Ctrl+Enter envia e abre o topo da fila ----------------------------------------------------------------------
  await confere('S3f (Node): proximaDaFila devolve o topo de "Precisa de você" que não é a atual, com canal e responsável, sem aba nem busca', async () => {
    const { registerHooks } = await import('node:module')
    // O console importa sem extensão (o Vite resolve); o Node precisa de ajuda, como na prova-1427.
    registerHooks({ resolve(especificador, contexto, proximo) {
      try { return proximo(especificador, contexto) } catch (erro) {
        if (especificador.startsWith('.') && !/\.m?jsx?$/.test(especificador)) return proximo(especificador + '.js', contexto)
        throw erro
      }
    } })
    const { proximaDaFila } = await import('../src/dominio/conversa.js')
    const AGORA = Date.parse('2026-10-07T12:00:00-03:00')
    const atras = (m) => new Date(AGORA - m * 60000).toISOString()
    const conversa = (id, minutos, extra = {}) => ({ id, nome: id, canal: 'WhatsApp', estado: 'Em atendimento', bloqueio: null, pedido: null, ultimaEm: atras(minutos),
      mensagens: [{ id: `${id}-1`, dir: 'in', texto: 'Tem ravióli hoje?', em: atras(minutos) }], passagem: { motivo: 'x', assumida: false }, ...extra })
    const filtros = { canais: [], busca: '', responsavel: 'todas', aba: 'todas', ordenacao: 'urgencia' }
    const fila = [conversa('recente', 7, { slaMinutos: 30 }), conversa('antiga', 20, { slaMinutos: 30 }), conversa('estourada', 6, { slaMinutos: 5 })]
    // o mesmo topo da aba (ordenarBalcao): SLA estourado primeiro, depois quem espera há mais tempo
    assert.equal(proximaDaFila(fila, 'recente', filtros, AGORA), 'estourada')
    // exclui a atual e segue a ordem
    assert.equal(proximaDaFila(fila, 'estourada', filtros, AGORA), 'antiga')
    assert.equal(proximaDaFila(fila, 'antiga', filtros, AGORA), 'estourada')
    assert.equal(proximaDaFila(fila, 'estourada', { ...filtros, ordenacao: 'recentes' }, AGORA), 'recente')
    // sem candidata: fila vazia, só a atual, ou só quem não entra na fila (encerrada e bloqueada)
    assert.equal(proximaDaFila([], 'x', filtros, AGORA), null)
    assert.equal(proximaDaFila([fila[0]], 'recente', filtros, AGORA), null)
    assert.equal(proximaDaFila([fila[0], conversa('fechada', 30, { estado: 'Encerrado' }), conversa('barrada', 40, { bloqueio: { motivo: 'x' } })], 'recente', filtros, AGORA), null)
    // ignora a aba exibida e a busca
    assert.equal(proximaDaFila(fila, 'recente', { ...filtros, aba: 'precisa' }, AGORA), 'estourada')
    assert.equal(proximaDaFila(fila, 'recente', { ...filtros, busca: 'zzzz' }, AGORA), 'estourada')
    // respeita o canal e o responsável ligados
    const mista = [conversa('zap', 20), conversa('insta', 30, { canal: 'Instagram' }), conversa('dela', 40, { responsavel: 'Dona' })]
    assert.equal(proximaDaFila(mista, 'zap', filtros, AGORA), 'dela', 'sem filtro, a mais antiga é a de Dona')
    assert.equal(proximaDaFila(mista, 'zap', { ...filtros, canais: ['Instagram'] }, AGORA), 'insta')
    assert.equal(proximaDaFila(mista, 'zap', { ...filtros, responsavel: 'sem-dono' }, AGORA), 'insta')
    assert.equal(proximaDaFila(mista, 'insta', { ...filtros, canais: ['Instagram'] }, AGORA), null)
  })

  await confere('S3a (demonstração): Ctrl+Enter com texto envia e abre o topo de "Precisa de você", com o foco no campo', async () => {
    const { contexto, pagina } = await abrirDemo({ comSessao: true, confirmado: true })
    try {
      const atual = await idAberto(pagina)
      const topo = await pagina.locator(CARTOES).first().getAttribute('data-conversa-id')
      assert.notEqual(topo, atual, 'a demonstração abriu o topo da fila: o caso não prova nada')
      await digitarEPressionar(pagina, FRASE, 'Control+Enter')
      assert.equal(await idAberto(pagina), topo, `a conversa aberta é ${await idAberto(pagina)}, não o topo da fila (${topo})`)
      assert.ok(await pagina.evaluate(ehOCampo), 'o foco não caiu no campo da conversa nova')
      assert.equal(await campoDe(pagina).inputValue(), '', 'o campo da conversa nova veio com texto')
      assert.equal(await textoNoFio(pagina, FRASE), 0, 'a mensagem apareceu no fio da conversa nova')
      // a mensagem saiu: está no fio da conversa de antes
      await pagina.getByRole('button', { name: /^Todas/ }).click()
      await pagina.locator(`${CARTOES}[data-conversa-id="${atual}"]`).click()
      await pagina.locator('[role=log]', { hasText: FRASE }).first().waitFor({ timeout: 5000 })
    } finally { await contexto.close() }
  })

  await confere('S3b (demonstração): Enter sozinho envia e a conversa aberta continua a mesma', async () => {
    const { contexto, pagina } = await abrirDemo({ comSessao: true, confirmado: true })
    try {
      const atual = await idAberto(pagina)
      await digitarEPressionar(pagina, FRASE, 'Enter')
      assert.equal(await textoNoFio(pagina, FRASE), 1, 'a mensagem não está no fio da conversa aberta: não enviou, ou enviou e trocou de conversa')
      assert.equal(await idAberto(pagina) ?? atual, atual, 'o Enter sozinho trocou de conversa')
      assert.ok(await pagina.evaluate(ehOCampo), 'o foco saiu do campo')
    } finally { await contexto.close() }
  })

  await confere('S3c (demonstração): acima do limite de 4096, Ctrl+Enter não envia nem troca de conversa', async () => {
    const { contexto, pagina } = await abrirDemo({ comSessao: true, confirmado: true })
    try {
      const atual = await idAberto(pagina)
      await digitarEPressionar(pagina, 'a'.repeat(4097), 'Control+Enter')
      assert.equal(await idAberto(pagina), atual, 'trocou de conversa com o texto acima do limite')
      assert.equal(await textoNoFio(pagina, 'aaaaaaaaaa'), 0, 'o texto acima do limite saiu')
      assert.equal((await campoDe(pagina).inputValue()).length, 4097, 'o campo perdeu o texto ou ganhou uma quebra de linha')
    } finally { await contexto.close() }
  })

  await confere('S3g (demonstração): Ctrl+Shift+Enter e Ctrl+Alt+Enter não enviam; Shift+Enter quebra a linha', async () => {
    const { contexto, pagina } = await abrirDemo({ comSessao: true, confirmado: true })
    try {
      const atual = await idAberto(pagina)
      for (const combinacao of ['Control+Shift+Enter', 'Control+Alt+Enter']) {
        await digitarEPressionar(pagina, FRASE, combinacao)
        assert.equal(await idAberto(pagina), atual, `${combinacao} trocou de conversa`)
        assert.equal(await textoNoFio(pagina, FRASE), 0, `${combinacao} enviou`)
      }
      await campoDe(pagina).fill('linha 1')
      await pagina.keyboard.press('Shift+Enter')
      await pagina.keyboard.type('linha 2')
      assert.equal(await campoDe(pagina).inputValue(), 'linha 1\nlinha 2', 'Shift+Enter não quebrou a linha')
      assert.equal(await idAberto(pagina), atual, 'Shift+Enter trocou de conversa')
    } finally { await contexto.close() }
  })

  await confere('S3d (demonstração, 1ª vez da sessão): Ctrl+Enter abre o modal, o Enter que confirma envia e avança', async () => {
    const { contexto, pagina } = await abrirDemo({ comSessao: true, confirmado: false })
    try {
      const atual = await idAberto(pagina)
      const topo = await pagina.locator(CARTOES).first().getAttribute('data-conversa-id')
      await digitarEPressionar(pagina, FRASE, 'Control+Enter')
      await dialogoDoModal(pagina).waitFor({ timeout: 3000 })
      assert.equal(await idAberto(pagina), atual, 'trocou de conversa antes de confirmar')
      assert.equal(await textoNoFio(pagina, FRASE), 0, 'enviou antes de confirmar')
      await pagina.keyboard.press('Enter')
      await pagina.waitForTimeout(700)
      assert.equal(await dialogoDoModal(pagina).count(), 0, 'o modal não fechou')
      assert.equal(await idAberto(pagina), topo, 'o Enter do modal não avançou para o topo da fila')
      assert.ok(await pagina.evaluate(ehOCampo), 'o foco não caiu no campo da conversa nova')
    } finally { await contexto.close() }
  })

  await confere('S3d2 (demonstração): fechar o modal apaga a intenção de avançar; o Enter seguinte só envia', async () => {
    const { contexto, pagina } = await abrirDemo({ comSessao: true, confirmado: false })
    try {
      const atual = await idAberto(pagina)
      await digitarEPressionar(pagina, FRASE, 'Control+Enter')
      await dialogoDoModal(pagina).waitFor({ timeout: 3000 })
      await pagina.keyboard.press('Escape')
      await pagina.waitForTimeout(400)
      assert.equal(await dialogoDoModal(pagina).count(), 0, 'o Esc não fechou o modal')
      assert.equal(await idAberto(pagina), atual, 'trocou de conversa ao fechar o modal')
      await campoDe(pagina).focus()
      await pagina.keyboard.press('Enter')
      await dialogoDoModal(pagina).waitFor({ timeout: 3000 })
      await pagina.keyboard.press('Enter')
      await pagina.waitForTimeout(700)
      assert.equal(await textoNoFio(pagina, FRASE), 1, 'o Enter depois de fechar o modal enviou fora da conversa de antes: a intenção de Ctrl+Enter ficou')
    } finally { await contexto.close() }
  })

  await confere('S3e (modo API): sem outra conversa na fila, Ctrl+Enter ou Meta+Enter envia e fica na conversa', async () => {
    // Uma só conversa esperando a atendente: não há "próxima", então o envio não pode trocar de conversa nem tirar o foco do campo.
    const { contexto, pagina } = await abrirApi({ quantas: 1, naoLidas: 1, todas: false, abrir: 'Pessoa 00', naFilaDaEquipe: true })
    try {
      assert.equal(await pagina.locator(CARTOES).count(), 1, 'a fila de teste tinha de ter uma só conversa')
      // Na rede simulada a lista de mensagens não guarda o que foi enviado; o que prova o envio é o POST.
      let envios = 0
      pagina.on('request', (r) => { if (r.method() === 'POST' && new URL(r.url()).pathname.endsWith('/mensagens')) envios += 1 })
      const sozinha = await idAberto(pagina)
      assert.ok(sozinha, 'sem conversa aberta na lista')
      for (const [frase, combinacao] of [[FRASE, 'Control+Enter'], ['Segunda resposta', 'Meta+Enter']]) {
        await digitarEPressionar(pagina, frase, combinacao)
        assert.equal(envios, frase === FRASE ? 1 : 2, `${combinacao}: a mensagem não foi enviada (POSTs de mensagem: ${envios})`)
        assert.equal(await idAberto(pagina), sozinha, `${combinacao} trocou de conversa sem haver outra`)
        assert.ok(await pagina.evaluate(ehOCampo), `${combinacao}: o foco saiu do campo`)
      }
    } finally { await contexto.close() }
  })
  // S4: a conversa respondida fica no lugar, atenuada, até a troca de conversa.
  const posicoes = (pagina) => pagina.evaluate((c) => Object.fromEntries([...document.querySelectorAll(c)].map((x) => [x.dataset.conversaId, Math.round(x.getBoundingClientRect().top * 10) / 10])), CARTOES)
  const corDoNome = (pagina, id) => pagina.evaluate(([c, alvo]) => getComputedStyle(document.querySelector(`${c}[data-conversa-id="${alvo}"] strong`)).color, [CARTOES, id])
  const rotuloDe = (pagina, id) => pagina.locator(`${CARTOES}[data-conversa-id="${id}"]`).getAttribute('aria-label')

  await confere('S4a (demonstração): ao responder, nenhum cartão se desloca e o da conversa aberta fica atenuado, com o estado no nome acessível', async () => {
    const { contexto, pagina } = await abrirDemo({ comSessao: true, confirmado: true })
    try {
      const aberta = await idAberto(pagina)
      const antes = await posicoes(pagina)
      const corAntes = await corDoNome(pagina, aberta)
      const campo = campoDe(pagina)
      await campo.click()
      await campo.fill(FRASE)
      await pagina.keyboard.press('Enter')
      // A resposta e a saída de "Precisa de você" chegam em momentos diferentes (a demonstração leva ~1 s): mede em todos.
      let depois = antes
      for (let n = 0; n < 24; n += 1) {
        await pagina.waitForTimeout(100)
        depois = await posicoes(pagina)
        assert.deepEqual(Object.keys(depois), Object.keys(antes), 'a lista ganhou, perdeu ou reordenou cartões ao responder')
        for (const id of Object.keys(antes)) assert.equal(depois[id], antes[id], `o cartão ${id} se deslocou ${depois[id] - antes[id]} px ${(n + 1) * 100} ms depois de responder`)
      }
      assert.equal(await idAberto(pagina), aberta, 'a conversa aberta mudou ao responder')
      const rotulo = await rotuloDe(pagina, aberta)
      assert.match(rotulo, /saiu desta lista.*conversa aberta/, `o nome acessível não diz que saiu da lista: ${rotulo}`)
      assert.notEqual(await corDoNome(pagina, aberta), corAntes, 'o cartão da conversa respondida não ficou atenuado')
      const outros = Object.keys(antes).filter((id) => id !== aberta)
      assert.doesNotMatch(await rotuloDe(pagina, outros[0]), /saiu desta lista/, 'outro cartão foi marcado como fora da lista')
    } finally { await contexto.close() }
  })

  await confere('S4b (demonstração): ao trocar de conversa a lista reordena e o cartão respondido sai', async () => {
    const { contexto, pagina } = await abrirDemo({ comSessao: true, confirmado: true })
    try {
      const aberta = await idAberto(pagina)
      await digitarEPressionar(pagina, FRASE, 'Enter')
      const parada = await posicoes(pagina)
      const outra = Object.keys(parada).find((id) => id !== aberta)
      await pagina.locator(`${CARTOES}[data-conversa-id="${outra}"]`).click()
      await pagina.mouse.move(1, 1)
      await pagina.waitForTimeout(900)
      const depois = await posicoes(pagina)
      assert.ok(!(aberta in depois), 'o cartão respondido continuou na lista depois da troca de conversa')
      assert.equal(await idAberto(pagina), outra, 'a conversa nova não ficou aberta')
      assert.equal(Object.keys(depois).length, Object.keys(parada).length - 1, 'a lista não reordenou na troca')
    } finally { await contexto.close() }
  })

  await confere('S4c (demonstração): mudar o filtro com a conversa aberta recomeça a lista, sem cartão preso', async () => {
    const { contexto, pagina } = await abrirDemo({ comSessao: true, confirmado: true })
    try {
      await digitarEPressionar(pagina, FRASE, 'Enter')
      await pagina.getByRole('button', { name: /^Todas/ }).click()
      await pagina.waitForTimeout(500)
      assert.equal(await pagina.locator(`${CARTOES}[aria-label*="saiu desta lista"]`).count(), 0, 'sobrou cartão marcado como fora da lista depois de mudar a aba')
      assert.ok((await pagina.locator(CARTOES).count()) >= 35, 'a aba Todas não mostrou as 35 conversas')
    } finally { await contexto.close() }
  })
  await confere('S1e (API): depois de andar pelas setas, o Tab sai da lista (a parada é o cartão focado, não o aberto)', async () => {
    const { contexto, pagina } = await abrirApi()
    try {
      const cartoes = pagina.locator(CARTOES)
      await cartoes.nth(2).focus()
      await pagina.keyboard.press('ArrowDown')
      assert.equal(await indiceDoFoco(pagina), 3, 'a seta não moveu o foco para o cartão 3')
      await pagina.keyboard.press('Tab')
      assert.equal(await pagina.evaluate(() => Boolean(document.activeElement?.closest('[role=list]'))), false, 'o Tab caiu em outro cartão da lista em vez de sair dela')
      // Voltando à lista, a parada é o cartão da conversa aberta (S1).
      await pagina.keyboard.press('Shift+Tab')
      assert.ok(await focoNoCartaoAberto(pagina), 'o Shift+Tab de volta não parou no cartão da conversa aberta')
    } finally { await contexto.close() }
  })

  await confere('S3h (modo API): trocar de conversa enquanto o envio assume cancela o avanço e não leva o foco', async () => {
    // c0 com o Assistente atendendo (enviar assume antes, 1,5 s) e c1, c2 na fila da equipe (a mais antiga, c2, é o topo).
    // Ctrl+Enter na c0 e, no meio, ela abre a c1: o avanço é da c0 e iria ao topo (c2), não à c1 que ela escolheu.
    const { contexto, pagina } = await abrirApi({ quantas: 3, naoLidas: 3, todas: true, abrir: 'Pessoa 00', naFilaDaEquipe: true, primeiraAutomatica: true, atrasoAssumirMs: 1500 })
    try {
      assert.equal(await idAberto(pagina), 'c0', 'a c0 não estava aberta')
      let assumiu = 0
      pagina.on('request', (r) => { if (r.method() === 'POST' && new URL(r.url()).pathname.endsWith('/assumir')) assumiu += 1 })
      const campo = campoDe(pagina)
      await campo.click(); await campo.fill(FRASE)
      await pagina.keyboard.press('Control+Enter')
      await pagina.waitForTimeout(300)
      assert.equal(assumiu, 1, 'o envio não pediu para assumir: o cenário não reproduz a espera')
      await pagina.locator(`${CARTOES}[data-conversa-id="c1"]`).click()
      await campoDe(pagina).waitFor()
      await pagina.keyboard.type('para a c1')
      await pagina.waitForTimeout(2500)
      assert.equal(await idAberto(pagina), 'c1', 'o avanço da c0 trocou a conversa que a atendente tinha aberto depois')
      assert.equal(await campoDe(pagina).inputValue(), 'para a c1', 'o que ela digitou na c1 foi perdido ou foi para outra conversa')
    } finally { await contexto.close() }
  })
} finally {
  await navegador.close()
  for (const servidor of servidores) await servidor.close()
}
console.log(falhas.length === 0 ? '\nprova-3363: tudo ok' : `\nprova-3363: ${falhas.length} falharam`)
process.exit(falhas.length > 0 ? 1 : 0)
