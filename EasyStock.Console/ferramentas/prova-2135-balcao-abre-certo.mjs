/* eslint-disable no-console */
// Prova da #2135 (Onda 0 do plano de UX do Balcão, #2130): o Balcão abre certo.
//
//   1  a tela abre SEM conversa selecionada (antes: a 34ª linha da massa, fora da lista visível), e
//      a sincronização da API nunca escolhe a primeira do servidor, que pode estar fora da fila;
//   2  abrir uma conversa pelo cartão leva o foco ao campo de mensagem no desktop (clique e teclado)
//      e não no celular, para não abrir o teclado sem pedido;
//   3  "Precisa de você" tem UMA decisão (`dominio/automatico.js`): o chip conta o que o cartão
//      destaca, o modo API segue a pausa que o servidor manda e conversa encerrada ou bloqueada não
//      conta (a lista de cima nunca a mostra);
//   4  o controle da loja no topo do celular tem alvo de 44 x 44 px e rótulo visível.
//
// Domínio e reducer rodam no node. As partes de tela precisam de Chromium: com PLAYWRIGHT_MODULE
// (a pasta do pacote playwright) a prova compila a demonstração em uma pasta temporária e a serve
// pelo próprio roteiro, sem servidor de pé. Sem a variável, essa parte é pulada e dita.
//
//   node ferramentas/prova-2135-balcao-abre-certo.mjs
//   PLAYWRIGHT_MODULE=<pasta do playwright> node ferramentas/prova-2135-balcao-abre-certo.mjs

import { registerHooks, createRequire } from 'node:module'
import assert from 'node:assert/strict'
import { mkdtempSync, readFileSync, readdirSync, statSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  // `fonteDados.js` lê `import.meta.env` do Vite; no node o modo API é fixo.
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    return proximo(url, contexto)
  },
})

const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const acao = await import('../src/aplicacao/acoes.js')
const { conversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { motivoDePrecisar, precisaDeVoce, contarPrecisaDeVoce, respostaAtrasada } = await import('../src/dominio/automatico.js')
const { conversasDoBalcao } = await import('../src/dominio/conversa.js')
const { classesDoCartao } = await import('../src/features/caixa-de-entrada/classesDoCartao.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

const AGORA = Date.parse('2026-10-28T15:00:00Z')
const USUARIO = { id: 'u-1', nome: 'Thati' }
const minutosAtras = (min) => new Date(AGORA - min * 60000).toISOString()
const resumo = (id, extra = {}) => ({
  id, canal: 'WhatsApp', contatoNome: `Cliente ${id}`, contatoIdExterno: `55119${id}`, clienteId: null,
  situacao: 'Aberta', assumidaPorUsuarioId: null, naoLidas: 1, dentroDaJanela: true,
  ultimaMensagemEm: minutosAtras(30), ultimaMensagemTexto: 'oi', aguardaResposta: true,
  ultimaMensagemEntradaEm: minutosAtras(30), pedidoEmAndamentoId: null, ...extra,
})
const entrada = (id, texto, minutos = 30) => ({
  id, direcao: 'Entrada', texto, criadaEm: minutosAtras(minutos), status: 'Recebida',
})
const daApi = (id, texto, extra = {}) => conversaDaApi(resumo(id, extra), [entrada('m-' + id, texto)], USUARIO, AGORA)
const sincronizar = (estado, conversas) => reducer(estado, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas })
const vazio = () => estadoInicial({ conversas: [], catalogo: { cardapio: [], janelas: [], canais: [] }, regras: [] })

// --- 1. Abre sem escolher por ela ------------------------------------------------------------
await confere('1. o estado inicial não seleciona conversa nenhuma, mesmo com a lista cheia', () => {
  const conversas = [daApi('c-1', 'oi?'), daApi('c-2', 'olá?')]
  const inicio = estadoInicial({ conversas, catalogo: { cardapio: [], janelas: [], canais: [] }, regras: [] })
  assert.equal(inicio.selecionadaId, null)
})

await confere('1. a primeira leitura da API não escolhe a primeira do servidor', () => {
  const lido = sincronizar(vazio(), [daApi('c-1', 'oi?'), daApi('c-2', 'olá?')])
  assert.equal(lido.conversas.length, 2)
  assert.equal(lido.selecionadaId, null)
})

await confere('1. a escolhida segue escolhida; se ela sai da lista, nenhuma fica escolhida', () => {
  const lido = sincronizar(vazio(), [daApi('c-1', 'oi?'), daApi('c-2', 'olá?')])
  const escolhida = { ...lido, selecionadaId: 'c-2' }
  assert.equal(sincronizar(escolhida, [daApi('c-1', 'oi?'), daApi('c-2', 'olá?')]).selecionadaId, 'c-2')
  assert.equal(sincronizar(escolhida, [daApi('c-1', 'oi?')]).selecionadaId, null)
})

// --- 3. Uma decisão só -----------------------------------------------------------------------
await confere('3. o modo API segue a pausa do servidor, não a que o chamador supõe', () => {
  // Automático ligado no servidor e o cliente disse só "obrigada": não é pergunta, ele ainda conduz.
  const ligado = daApi('c-3', 'Obrigada!', { situacao: 'Aberta' })
  assert.equal(ligado.pausaApi, false)
  assert.equal(motivoDePrecisar(ligado, AGORA, false), null)
  assert.equal(motivoDePrecisar(ligado, AGORA, true), null, 'o chamador dizer "pausado" não pode contradizer o servidor')
  assert.equal(respostaAtrasada(ligado, AGORA, true), false)
  // Ela assumiu no servidor: o silêncio já é dela, mesmo que o chamador não saiba.
  const assumida = daApi('c-4', 'Obrigada!', { situacao: 'Assumida', assumidaPorUsuarioId: USUARIO.id })
  assert.ok(assumida.pausaApi)
  assert.equal(motivoDePrecisar(assumida, AGORA, false)?.chave, 'esperando')
  assert.equal(respostaAtrasada(assumida, AGORA, false), true)
})

await confere('3. conversa encerrada ou bloqueada com passagem não conta: a lista de cima não a mostra', () => {
  const passada = { motivo: 'o cliente pediu uma pessoa', assumida: false }
  const base = { ...daApi('c-5', 'oi?'), passagem: passada }
  assert.equal(motivoDePrecisar(base, AGORA)?.chave, 'passagem')
  for (const fora of [{ estado: 'Encerrado' }, { bloqueio: { em: minutosAtras(1), motivo: 'x' } }]) {
    const conversa = { ...base, ...fora }
    assert.equal(precisaDeVoce(conversa, AGORA), false)
    assert.equal(contarPrecisaDeVoce([conversa], AGORA), 0)
  }
})

await confere('3. o contador do chip é igual ao tamanho da lista "Precisa de você"', () => {
  const conversas = [
    { ...daApi('c-6', 'oi?'), passagem: { motivo: 'restrição', assumida: false } },
    { ...daApi('c-7', 'olá?'), estado: 'Encerrado', passagem: { motivo: 'restrição', assumida: false } },
    daApi('c-8', 'Obrigada!'),
    daApi('c-9', 'Tem lasanha sem lactose?'),
  ]
  const filtros = { busca: '', aba: 'precisa', canais: [], ordenacao: 'urgencia', responsavel: 'todas' }
  const lista = conversasDoBalcao(conversas, filtros, AGORA, {}, true, [], null)
  assert.equal(contarPrecisaDeVoce(conversas, AGORA, {}, true, [], null), lista.length)
})

await confere('3. todo motivo de precisar vira destaque no cartão, de qualquer tom; sem motivo não há', () => {
  const css = { cartao: 'c', precisa: 'p', atrasada: 'a', slaEstourado: 's' }
  for (const tom of ['aviso', 'perigo', 'neutro']) {
    const classes = classesDoCartao(css, { motivo: { tom } }).split(' ')
    assert.ok(classes.includes('p') || classes.includes('a'), `tom ${tom} ficou sem marca: ${classes.join(' ')}`)
  }
  assert.equal(classesDoCartao(css, { motivo: null }), 'c')
  assert.equal(classesDoCartao(css, { motivo: { tom: 'perigo' } }), 'c a')
  assert.equal(classesDoCartao(css, { motivo: { tom: 'aviso' }, slaEstourado: true }), 'c p s')
})

await confere('3. nenhum chamador de "Precisa de você" inventa a pausa com um `true` fixo', () => {
  const achados = []
  const ler = (pasta) => {
    for (const nome of readdirSync(pasta)) {
      const caminho = join(pasta, nome)
      if (statSync(caminho).isDirectory()) { ler(caminho); continue }
      if (!/\.(js|jsx)$/.test(nome) || /dominio[\\/]automatico\.js$/.test(caminho)) continue
      const fonte = readFileSync(caminho, 'utf8')
      for (const m of fonte.matchAll(/\b(precisaDeVoce|motivoDePrecisar|motivoVisivel)\(\s*[^,()]+,\s*[^,()]+,\s*true\b/g)) {
        achados.push(`${caminho.split(/src[\\/]/)[1]}: ${m[0]}`)
      }
    }
  }
  ler(resolve(import.meta.dirname, '../src'))
  assert.deepEqual(achados, [])
})

// --- 2 e 4, e o contador na tela real --------------------------------------------------------
const pastaDoPlaywright = process.env.PLAYWRIGHT_MODULE
if (!pastaDoPlaywright) {
  console.log('\npulado: sem PLAYWRIGHT_MODULE não há Chromium; foco, contador na tela e loja do celular ficam sem conferência neste roteiro.')
} else {
  const { chromium } = createRequire(import.meta.url)(pastaDoPlaywright)
  const { build } = await import('vite')
  const pasta = mkdtempSync(join(tmpdir(), 'prova-2135-'))
  const raiz = resolve(import.meta.dirname, '..')
  await build({ root: raiz, logLevel: 'silent', build: { outDir: pasta, emptyOutDir: true } })
  const html = readFileSync(join(pasta, 'index.html'), 'utf8')
  const ORIGEM = 'http://localhost:5199'
  const navegador = await chromium.launch({ headless: true })

  async function abrir({ largura, altura = 900, celular = false }) {
    const contexto = await navegador.newContext({
      viewport: { width: largura, height: altura }, serviceWorkers: 'block', reducedMotion: 'reduce',
      ...(celular ? { hasTouch: true, isMobile: true } : {}),
    })
    await contexto.route(`${ORIGEM}/**`, (rota) => {
      const url = new URL(rota.request().url())
      if (url.pathname.startsWith('/api/')) return rota.fulfill({ json: { data: [] } })
      return rota.fulfill({ body: html, contentType: 'text/html' })
    })
    const pagina = await contexto.newPage()
    const erros = []
    pagina.on('pageerror', (erro) => erros.push(erro.message))
    await pagina.goto(`${ORIGEM}/#/m/atendimento`)
    await pagina.locator('button[aria-current]').first().waitFor()
    return { contexto, pagina, erros }
  }

  const cartoes = (pagina) => pagina.locator('button[aria-current]').filter({ has: pagina.locator('time') })
  // Destacado é o que a atendente enxerga: o trilho lateral do cartão (box-shadow interno).
  const medirCartoes = (pagina) => pagina.evaluate(() => {
    const itens = [...document.querySelectorAll('button[aria-current]')].filter((b) => b.querySelector('time'))
    return itens.map((b) => ({
      nome: b.querySelector('strong')?.textContent.trim() ?? '',
      selecionado: b.getAttribute('aria-current') === 'true',
      destacado: getComputedStyle(b).boxShadow.includes('inset'),
      topo: Math.round(b.getBoundingClientRect().top + scrollY),
    }))
  })
  const campoComFoco = (pagina) => pagina.evaluate(() => {
    const ativo = document.activeElement
    return Boolean(ativo) && ativo.tagName === 'TEXTAREA' && ativo.labels?.[0]?.textContent === 'Mensagem para o cliente'
  })
  const numeroDoChip = async (pagina) => {
    const texto = await pagina.getByRole('button', { name: /Precisa de você/ }).first().innerText()
    return Number(texto.match(/(\d+)\s*$/)?.[1])
  }

  try {
    await confere('1. desktop: abre sem conversa escolhida e convida a escolher', async () => {
      const { contexto, pagina } = await abrir({ largura: 1280, altura: 900 })
      const medidos = await medirCartoes(pagina)
      assert.ok(medidos.length > 20, 'a massa da demonstração precisa ter lista longa')
      const escolhidos = medidos.filter((c) => c.selecionado)
      assert.deepEqual(escolhidos.map((c) => c.nome), [], 'a tela abriu numa conversa que ela não escolheu')
      assert.equal(await pagina.getByText('Escolha uma conversa').count(), 1)
      await contexto.close()
    })

    await confere('3. desktop: o chip "Precisa de você" é igual aos cartões destacados', async () => {
      const { contexto, pagina } = await abrir({ largura: 1280, altura: 900 })
      const chip = await numeroDoChip(pagina)
      const medidos = await medirCartoes(pagina)
      const destacados = medidos.filter((c) => c.destacado)
      assert.ok(chip > 0)
      assert.ok(medidos.some((c) => !c.destacado), 'ao menos um cartão fica sem marca para a medida valer')
      assert.equal(destacados.length, chip, `chip ${chip}, destacados ${destacados.length}: ${destacados.map((c) => c.nome).join(', ')}`)
      // E o filtro mostra exatamente o que o chip contou, todos destacados.
      await pagina.getByRole('button', { name: /Precisa de você/ }).first().click()
      const filtrados = await medirCartoes(pagina)
      assert.equal(filtrados.length, chip)
      assert.ok(filtrados.every((c) => c.destacado))
      await contexto.close()
    })

    await confere('2. desktop: abrir pelo clique leva o foco ao campo de mensagem', async () => {
      const { contexto, pagina } = await abrir({ largura: 1280, altura: 900 })
      await cartoes(pagina).filter({ hasText: 'José Moretti' }).click()
      assert.equal(await campoComFoco(pagina), true, 'o foco ficou no cartão')
      await contexto.close()
    })

    await confere('2. desktop: abrir pelo teclado (Enter no cartão) leva o foco ao campo', async () => {
      const { contexto, pagina } = await abrir({ largura: 1280, altura: 900 })
      const cartao = cartoes(pagina).filter({ hasText: 'Heloísa Prado' })
      await cartao.focus()
      await pagina.keyboard.press('Enter')
      assert.equal(await campoComFoco(pagina), true, 'o foco ficou no cartão')
      await contexto.close()
    })

    await confere('2. desktop: clicar de novo na conversa já aberta devolve o foco ao campo', async () => {
      const { contexto, pagina } = await abrir({ largura: 1280, altura: 900 })
      const cartao = cartoes(pagina).filter({ hasText: 'José Moretti' })
      await cartao.click()
      await pagina.getByRole('heading', { level: 2 }).first().focus()
      await cartao.click()
      assert.equal(await campoComFoco(pagina), true)
      await contexto.close()
    })

    await confere('2. celular: abrir a conversa não põe o foco no campo (o teclado não abre sozinho)', async () => {
      const { contexto, pagina } = await abrir({ largura: 390, altura: 844, celular: true })
      await cartoes(pagina).filter({ hasText: 'José Moretti' }).tap()
      await pagina.getByRole('textbox', { name: 'Mensagem para o cliente' }).waitFor()
      assert.equal(await campoComFoco(pagina), false)
      await contexto.close()
    })

    for (const largura of [390, 375]) {
      await confere(`4. celular ${largura}: controle da loja com alvo de 44 x 44 px e sem estourar a linha`, async () => {
        const { contexto, pagina } = await abrir({ largura, altura: 844, celular: true })
        const loja = pagina.getByRole('button', { name: /^Loja (aberta|fechada)$/ })
        const caixa = await loja.boundingBox()
        assert.ok(caixa.width >= 44 && caixa.height >= 44, `alvo de ${Math.round(caixa.width)} x ${Math.round(caixa.height)} px`)
        const sobra = await pagina.evaluate(() => document.documentElement.scrollWidth - innerWidth)
        assert.ok(sobra <= 0, `a página passou ${sobra} px da largura`)
        const mais = await pagina.getByRole('button', { name: 'Mais', exact: true }).boundingBox()
        assert.ok(mais.x + mais.width <= largura, 'o botão Mais saiu da tela')
        await contexto.close()
      })
    }

    await confere('4. celular 390: o controle da loja diz o estado por escrito, e o nome acessível não muda', async () => {
      const { contexto, pagina } = await abrir({ largura: 390, altura: 844, celular: true })
      const loja = pagina.getByRole('button', { name: /^Loja (aberta|fechada)$/ })
      const visivel = (await loja.evaluate((el) => el.innerText)).trim()
      assert.match(visivel, /^(Aberta|Fechada)$/)
      assert.ok(await loja.getAttribute('title'), 'sem dica')
      assert.equal(await loja.getAttribute('aria-pressed'), 'true')
      await contexto.close()
    })

    await confere('4. desktop: o controle da loja do trilho segue com o nome e o texto de antes', async () => {
      const { contexto, pagina } = await abrir({ largura: 1280, altura: 900 })
      const loja = pagina.getByRole('button', { name: /^Loja (aberta|fechada)$/ })
      assert.equal(await loja.count(), 1)
      assert.match((await loja.innerText()).trim(), /^Loja (aberta|fechada)$/)
      await contexto.close()
    })
  } finally {
    await navegador.close()
    rmSync(pasta, { recursive: true, force: true })
  }
}

console.log(`\n${passou} verificações passaram${falhas.length ? `, ${falhas.length} falharam` : ''}.`)
if (falhas.length) {
  falhas.forEach((f) => console.log('  - ' + f))
  process.exit(1)
}
