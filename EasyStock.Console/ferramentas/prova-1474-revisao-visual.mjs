/* eslint-disable no-console */
// Prova da issue #1474 (revisão visual clicando no produto real, modo API):
//   C1  nota interna do sistema vira evento, não balão "enviando" eterno nem "Automático:";
//   C2  variável sem valor ({pedido}) não sai literal ao cliente;
//   C3  dossiê do cliente carrega também para a conversa restaurada, sem chamada duplicada;
//   C4  erro da API mostra o `detail` útil no lugar de "Requisição inválida";
//   C5  baixa manual com cobrança online pendente cancela o link (na_entrega) antes do pagamento;
//   C6  legenda da foto sem peso duplicado e sem "Foto N";
//   C9  saldo sem valor vira "—", não "null porções";
//   C11 automáticas de entrada dizem a verdade sobre a saudação; carregando/erro/vazio separados;
//   C12 sugestão de tag já presente some; "cliente_vip" aparece "cliente vip";
//   C13 aviso velho some quando a ação seguinte dá certo; vitrine desligada vira texto acionável;
//   C14 pedido novo nasce sem janela inventada;
//   C16 gaveta (dinheiro) separada de Pix e cartão; lançamentos em ordem de hora;
//   C17 canal com mensagem recebida diz "Recebendo mensagens";
//   C18 cozinha escolhe hoje ou amanhã;
//   C8, C19 rota de módulo da barra lateral e CEP com máscara.
//
//   node ferramentas/prova-1474-revisao-visual.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    if (url.endsWith('.json')) {
      return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    }
    return proximo(url, contexto)
  },
})

globalThis.sessionStorage = {
  getItem: () => JSON.stringify({ token: 't', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' } }),
  setItem: () => {}, removeItem: () => {},
}
globalThis.window = globalThis.window ?? { dispatchEvent: () => {} }

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
const esvaziar = async () => { for (let i = 0; i < 8; i += 1) await new Promise((r) => setTimeout(r, 0)) }

let respostaFetch = () => ({ data: null })
const chamadas = []
globalThis.fetch = async (url, opcoes = {}) => {
  const chamada = { url: String(url), metodo: opcoes.method ?? 'GET', corpo: opcoes.body ? JSON.parse(opcoes.body) : undefined }
  chamadas.push(chamada)
  const r = await respostaFetch(chamada)
  const status = r?.status ?? 200
  return new Response(JSON.stringify(r?.corpo ?? r), { status, headers: { 'Content-Type': 'application/json' } })
}

const acao = await import('../src/aplicacao/acoes.js')
const { reducer } = await import('../src/aplicacao/reducer.js')
const { mensagemDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { previaDaConversa } = await import('../src/dominio/conversa.js')
const { respondeAoCliente } = await import('../src/dominio/mensagem.js')
const respostas = await import('../src/dominio/respostas.js')
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const plano = await import('../src/aplicacao/planoDeSincronizacao.js')
const { criarAcoesClienteApi } = await import('../src/aplicacao/api/cliente.js')
const { chamarApi } = await import('../src/infra/api/cliente.js')
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const cobranca = await import('../src/dominio/cobranca.js')
const vitrine = await import('../src/dominio/vitrineCardapio.js')
const producao = await import('../src/dominio/producao.js')
const automacao = await import('../src/dominio/automacao.js')
const cliente = await import('../src/dominio/cliente.js')
const { novoPedido } = await import('../src/dominio/pedido.js')
const caixa = await import('../src/dominio/caixa.js')
const { resumoDoWhatsApp } = await import('../src/dominio/canais.js')
const kds = await import('../src/dominio/kds.js')
const kdsApi = await import('../src/infra/api/kdsApi.js')
const formato = await import('../src/dominio/formato.js')
const rota = await import('../src/dominio/rota.js')
const { criarAcoesRespostasApi } = await import('../src/aplicacao/api/respostas.js')

// ── C1 ─────────────────────────────────────────────────────────────────────
const NOTA_INTERNA = {
  id: 'n1', direcao: 'Saida', autor: 'Sistema', status: 'Pendente', externoId: null,
  texto: 'Escalado para a dona: agente desligado', enviadaEm: '2026-10-08T15:00:00', tipoConteudo: 'Texto',
}
await confere('C1 nota interna do sistema vira evento (dir sistema), sem status "enviando"', () => {
  const m = mensagemDaApi(NOTA_INTERNA, 'c1')
  assert.equal(m.dir, 'sistema')
  assert.equal(m.status, undefined)
  assert.equal(m.automatica, undefined)
})
await confere('C1 automática do sistema que saiu ao canal continua balão automático', () => {
  const m = mensagemDaApi({ ...NOTA_INTERNA, status: 'Enviada', externoId: 'wamid.1' }, 'c1')
  assert.equal(m.dir, 'out')
  assert.equal(m.automatica, true)
})
await confere('C1 nota interna não vira prévia "Automático:" nem resposta ao cliente', () => {
  const nota = mensagemDaApi(NOTA_INTERNA, 'c1')
  const { prefixo } = previaDaConversa({ mensagens: [nota] })
  assert.notEqual(prefixo, 'Automático: ')
  assert.equal(respondeAoCliente(nota), false)
})

// ── C2 ─────────────────────────────────────────────────────────────────────
await confere('C2 variaveisNaoResolvidas acha {pedido} e {nome} sobrando', () => {
  assert.deepEqual(respostas.variaveisNaoResolvidas('Pedido {pedido}, {nome}. {pedido}'), ['{pedido}', '{nome}'])
  assert.deepEqual(respostas.variaveisNaoResolvidas('Tudo certo, Ana.'), [])
  assert.equal(respostas.avisoDeVariaveisNaoResolvidas('Pedido {pedido}, {nome}.'),
    'Esta conversa não tem número do pedido: apague {pedido} ou escreva no lugar. '
    + 'Esta conversa não tem nome do cliente: apague {nome} ou escreva no lugar.')
  assert.equal(respostas.avisoDeVariaveisNaoResolvidas('ok'), null)
})
await confere('C2 enviar no modo API não manda texto com variável e avisa', async () => {
  const despachos = []
  chamadas.length = 0
  const api = comApi({}, { despachar: (a) => despachos.push(a), agoraRef: { current: Date.now() }, estadoRef: { current: { conversas: [] } } })
  api.enviar('c1', 'Pedido {pedido}.')
  await esvaziar()
  assert.equal(chamadas.length, 0)
  assert.equal(despachos.length, 1)
  assert.equal(despachos[0].tipo, acao.AVISO_API)
  assert.match(despachos[0].mensagem, /\{pedido\}/)
})

// ── C3 ─────────────────────────────────────────────────────────────────────
await confere('C3 deveCarregarDossie: cliente vinculado sem dossiê da API', () => {
  assert.equal(plano.deveCarregarDossie({ clienteId: 'cl', cliente: { tags: [] } }), true)
  assert.equal(plano.deveCarregarDossie({ clienteId: 'cl', cliente: { daApi: true } }), false)
  assert.equal(plano.deveCarregarDossie({ clienteId: null, cliente: {} }), false)
  assert.equal(plano.deveCarregarDossie(null), false)
})
await confere('C3 dossiê pedido duas vezes seguidas sai numa chamada só', async () => {
  chamadas.length = 0
  let liberar
  const espera = new Promise((r) => { liberar = r })
  respostaFetch = async () => { await espera; return { data: { cliente: { id: 'cl', nome: 'Ana', telefone: '11999990000' }, enderecos: [], tags: [{ tag: 'lactose' }], notas: [] } } }
  const despachos = []
  const api = criarAcoesClienteApi({ despachar: (a) => despachos.push(a), estadoRef: { current: { conversas: [{ id: 'c1', clienteId: 'cl', cliente: {} }] } } })
  const a = api.carregarClienteDaConversa('c1')
  const b = api.carregarClienteDaConversa('c1')
  liberar()
  await Promise.all([a, b])
  assert.equal(chamadas.length, 1, chamadas.map((c) => c.url).join(', '))
  assert.equal(despachos.filter((d) => d.tipo === acao.CLIENTE_DA_API).length, 1)
  respostaFetch = () => ({ data: null })
})

// ── C4 ─────────────────────────────────────────────────────────────────────
await confere('C4 erro genérico "Requisição inválida" mostra o detail', async () => {
  respostaFetch = () => ({ status: 400, corpo: { error: { code: 'VALIDATION_ERROR', message: 'Requisição inválida', detail: 'Pedido pré-operacional não aceita pagamento manual.' } } })
  await assert.rejects(chamarApi('/x', { metodo: 'POST', corpo: {} }), (e) => {
    assert.equal(e.message, 'Pedido pré-operacional não aceita pagamento manual.')
    assert.equal(e.detalhe, 'Pedido pré-operacional não aceita pagamento manual.')
    return true
  })
})
await confere('C4 mensagem específica fica; sem detail fica a mensagem', async () => {
  respostaFetch = () => ({ status: 409, corpo: { error: { code: 'X', message: 'Janela lotada.', detail: null } } })
  await assert.rejects(chamarApi('/x'), (e) => e.message === 'Janela lotada.')
  respostaFetch = () => ({ status: 400, corpo: { error: { code: 'VALIDATION_ERROR', message: 'Requisição inválida' } } })
  await assert.rejects(chamarApi('/x'), (e) => e.message === 'Requisição inválida')
  respostaFetch = () => ({ data: null })
})

// ── C5 ─────────────────────────────────────────────────────────────────────
const PEDIDO_ONLINE = {
  pedidoId: '11111111-1111-1111-1111-111111111111', status: 'aguardando_pagamento', total: 25, frete: 5, itens: [],
  totalPago: 0, pagamentos: [],
  cobranca: { cobrancaId: 'cob', provedor: 'mercado_pago', status: 'Pendente', valor: 25, linkPagamento: 'https://mp/x', criadaEm: '2026-10-08T15:00:00Z', expiraEm: '2099-01-01T00:00:00Z' },
}
await confere('C5 baixa manual de cobrança online pendente cancela o link e pré-preenche o meio', () => {
  const pedido = pedidoDaApi(PEDIDO_ONLINE, { meio: 'pix' })
  assert.equal(cobranca.baixaCancelaCobrancaOnline(pedido), true)
  assert.equal(cobranca.metodoDaCobranca(pedido.cobranca), 'pix')
  assert.equal(cobranca.metodoDaCobranca({ meio: 'cartao-link' }), '')
  assert.equal(cobranca.metodoDaCobranca({ meio: 'cartao-link', metodoRecebido: 'credito' }), 'credito')
  assert.match(cobranca.AVISO_BAIXA_COM_LINK, /link do Mercado Pago será cancelado/)
  const naEntrega = pedidoDaApi({ ...PEDIDO_ONLINE, status: 'aguardando', cobranca: { ...PEDIDO_ONLINE.cobranca, provedor: 'na_entrega', linkPagamento: null } })
  assert.equal(cobranca.baixaCancelaCobrancaOnline(naEntrega), false)
})
await confere('C5 confirmar pagamento chama cobranca/forma na_entrega e depois pagamentos', async () => {
  chamadas.length = 0
  respostaFetch = () => ({ data: PEDIDO_ONLINE })
  const pedido = pedidoDaApi(PEDIDO_ONLINE, { meio: 'pix' })
  const despachos = []
  const api = criarAcoesComandaApi({}, { despachar: (a) => despachos.push(a), estadoRef: { current: { conversas: [{ id: 'c1', pedido }] } } })
  await api.confirmarPagamento('c1', 25, 'pix')
  const posts = chamadas.filter((c) => c.metodo === 'POST')
  assert.equal(posts.length, 2, posts.map((c) => c.url).join(', '))
  assert.ok(posts[0].url.endsWith('/cobranca/forma') && posts[0].corpo.forma === 'na_entrega', posts[0].url)
  assert.ok(posts[1].url.endsWith('/pagamentos') && posts[1].corpo.metodo === 'pix', posts[1].url)
  respostaFetch = () => ({ data: null })
})

// ── C6 ─────────────────────────────────────────────────────────────────────
await confere('C6 legendaDoItem omite a porção já presente no nome e inclui o preço', () => {
  assert.match(vitrine.legendaDoItem({ nome: 'Lasanha Bolonhesa Clássica 600 g', porcao: '600 g', preco: 38 }),
    /^Lasanha Bolonhesa Clássica 600 g\nR\$\s?38,00$/)
  assert.match(vitrine.legendaDoItem({ nome: 'Lasanha Bolonhesa', porcao: '600 g', preco: 38 }),
    /^Lasanha Bolonhesa\n600 g · R\$\s?38,00$/)
  assert.match(vitrine.legendaDoItem({ nome: 'Nhoque 500G', porcao: '500 g', preco: 10 }), /^Nhoque 500G\nR\$/)
})
await confere('C6 peça da galeria do cardápio leva legenda sem "Foto N"', () => {
  const item = { sku: 's1', nome: 'Lasanha Bolonhesa Clássica 600 g', porcao: '600 g', preco: 38, fotos: ['/f/a.webp', '/f/b.webp'] }
  const estado = reducer({ catalogo: { cardapio: [], galeria: [] } }, { tipo: acao.SINCRONIZAR_CARDAPIO, cardapio: [item] })
  const peca = estado.catalogo.galeria[1]
  assert.match(peca.descricao, /Foto 2/)
  assert.doesNotMatch(peca.legenda, /Foto/)
  assert.doesNotMatch(peca.legenda, /600 g ·/)
})

// ── C9 ─────────────────────────────────────────────────────────────────────
await confere('C9 saldo sem valor vira "—"', () => {
  assert.equal(producao.rotuloDoSaldo(null), '—')
  assert.equal(producao.rotuloDoSaldo(undefined), '—')
  assert.equal(producao.rotuloDoSaldo(1), '1 porção')
  assert.equal(producao.rotuloDoSaldo(0), '0 porções')
})

// ── C11 ────────────────────────────────────────────────────────────────────
await confere('C11 automáticas de entrada avisam da saudação e de duas mensagens', () => {
  // K7: a saudação é do primeiro contato; fora do horário leva a mensagem do expediente.
  const entrada = { gatilho: automacao.GATILHOS.PRIMEIRO_CONTATO, ativa: false, texto: '' }
  assert.equal(automacao.ehAutomaticaDeEntrada(entrada), true)
  assert.equal(automacao.ehAutomaticaDeEntrada({ gatilho: automacao.GATILHOS.PEDIDO_GERADO }), false)
  assert.equal(automacao.avisoDaAutomaticaDeEntrada(entrada),
    'Mesmo desligada, a primeira mensagem já leva a saudação de Horários e mensagens.')
  assert.match(automacao.avisoDaAutomaticaDeEntrada({ ...entrada, ativa: true, texto: 'Oi' }), /duas mensagens/)
})
await confere('C11 carga das automáticas: ok com lista vazia, erro quando falha', async () => {
  const ok = reducer({ regras: [] }, { tipo: acao.REGRAS_DA_API, regras: [] })
  assert.equal(ok.cargaDasRegras?.estado, 'ok')
  respostaFetch = () => ({ status: 500, corpo: { error: { message: 'Fora do ar' } } })
  const despachos = []
  const api = criarAcoesRespostasApi({ despachar: (a) => despachos.push(a), estadoRef: { current: { regras: [] } } })
  await api.recarregarRespostas()
  const falha = despachos.find((d) => d.tipo === acao.REGRAS_FALHARAM)
  assert.ok(falha, despachos.map((d) => d.tipo).join(', '))
  assert.equal(reducer({ regras: [] }, falha).cargaDasRegras.estado, 'erro')
  respostaFetch = () => ({ data: null })
})

// ── C12 ────────────────────────────────────────────────────────────────────
await confere('C12 sugestão já presente (sem acento/caixa) some; "_" vira espaço', () => {
  const s = cliente.sugestoesDeTag('', { conversas: [], cadastroIdAtual: null, tagsAtuais: ['lactose'], restricoes: ['Lactose', 'Glúten'] })
  assert.deepEqual(s.restricao, ['Glúten'])
  const s2 = cliente.sugestoesDeTag('', { conversas: [], cadastroIdAtual: null, tagsAtuais: ['gluten'], restricoes: ['Lactose', 'Glúten'] })
  assert.deepEqual(s2.restricao, ['Lactose'])
  assert.equal(cliente.rotuloDaTag('cliente_vip'), 'cliente vip')
})

// ── C13 ────────────────────────────────────────────────────────────────────
await confere('C13 vitrine desligada vira aviso acionável', () => {
  const texto = plano.avisoDoCardapio({ status: 404, message: 'A empresa não tem vitrine ativa.' })
  assert.equal(texto, 'A loja online está desligada: o cardápio não carrega até ligar a vitrine.')
  assert.equal(plano.avisoDoCardapio({ status: 500, message: 'Fora' }), 'Cardápio: Fora')
})
await confere('C13 escolher janela e criar pedido limpam o aviso velho', async () => {
  const despachos = []
  const conversa = { id: 'c1', pedido: { numero: 'x', janela: null, itens: [{ sku: 's', qtd: 1, obs: '' }], meio: 'pix' } }
  const estadoRef = { current: { conversas: [conversa] } }
  const api = criarAcoesComandaApi({ escolherJanela: () => {} }, { despachar: (a) => despachos.push(a), estadoRef })
  api.gerarPedido('c1')
  assert.match(despachos.at(-1).mensagem, /Escolha a janela/)
  api.escolherJanela('c1', 'j|2026-10-09')
  assert.equal(despachos.at(-1).tipo, acao.FECHAR_AVISO_API)
  despachos.length = 0
  conversa.pedido = { ...conversa.pedido, janela: 'j|2026-10-09' }
  respostaFetch = () => ({ data: { ...PEDIDO_ONLINE, enviadoAoCliente: true } })
  await api.gerarPedido('c1')
  assert.ok(despachos.some((d) => d.tipo === acao.FECHAR_AVISO_API), despachos.map((d) => d.tipo).join(', '))
  respostaFetch = () => ({ data: null })
})

// ── C14 ────────────────────────────────────────────────────────────────────
await confere('C14 pedido novo nasce sem janela', () => {
  assert.equal(novoPedido('1').janela, null)
})

// ── C16 ────────────────────────────────────────────────────────────────────
const DIA = {
  saldoInicial: 100, saldoEsperado: 180.5,
  movimentos: [
    { id: 'a', tipo: 'abertura', valor: 100, meio: null, em: '2026-10-08T20:00:00' },
    { id: 'e', tipo: 'entrada', valor: 10.5, meio: 'dinheiro', em: '2026-10-08T20:18:00' },
    { id: 's', tipo: 'saida', valor: 10, meio: 'dinheiro', em: '2026-10-08T20:30:00' },
    { id: 'x', tipo: 'saida', valor: 99, meio: 'dinheiro', em: '2026-10-08T20:31:00', estornadoEm: '2026-10-08T20:32:00' },
  ],
  linhasExtras: [
    { origem: 'Pedido', valor: 30, meio: 'dinheiro', em: '2026-10-08T20:28:00' },
    { origem: 'Pedido', valor: 50, meio: 'pix', em: '2026-10-08T20:10:00' },
  ],
}
await confere('C16 gaveta só com dinheiro; Pix e cartão à parte', () => {
  const g = caixa.gavetaDoDia(DIA)
  assert.equal(g.naGaveta, 130.5)
  assert.equal(g.pixECartao, 50)
})
await confere('C16 lançamentos em ordem de hora, juntando movimentos e pagamentos', () => {
  const ordem = caixa.lancamentosEmOrdem(DIA).map((l) => l.em.slice(11, 16))
  assert.deepEqual(ordem, ['20:00', '20:10', '20:18', '20:28', '20:30', '20:31'])
})

// ── C17 ────────────────────────────────────────────────────────────────────
await confere('C17 mensagem recebida: "Recebendo mensagens" e webhook funcionando', () => {
  const r = resumoDoWhatsApp({ phoneNumberId: '1', provider: 'meta', ultimaMensagemRecebidaEm: '2026-10-07T14:50:00Z' }, Date.parse('2026-10-07T15:00:00Z'))
  assert.ok(r.linhas.includes('Recebendo mensagens (última às 11:50 de 07/10)'), r.linhas.join(' | '))
  assert.ok(!r.linhas.some((l) => /não verificado/i.test(l)), r.linhas.join(' | '))
})

// ── C18 ────────────────────────────────────────────────────────────────────
await confere('C18 fila do KDS pede o dia escolhido', async () => {
  chamadas.length = 0
  respostaFetch = () => ({ data: [] })
  await kdsApi.listarPedidosKds('2026-10-09')
  await kdsApi.listarPedidosKds()
  assert.equal(chamadas[0].url, '/api/kds/pedidos?data=2026-10-09')
  assert.equal(chamadas[1].url, '/api/kds/pedidos')
  assert.match(kds.textoDaCozinhaVazia({ amanha: false }), /Amanhã/)
  assert.match(kds.textoDaCozinhaVazia({ amanha: true }), /amanhã/)
  respostaFetch = () => ({ data: null })
})

// ── C8 e C19 ───────────────────────────────────────────────────────────────
await confere('C8 barra lateral leva às rotas de módulo', () => {
  assert.equal(rota.rotaDaHash(rota.HASH_MODULO_COZINHA, { fonteApi: true }).modulo, 'cozinha')
  assert.equal(rota.rotaDaHash(rota.HASH_MODULO_ENTREGAS, { fonteApi: true }).modulo, 'entregas')
})
await confere('C19 faixa de CEP com máscara', () => {
  assert.equal(formato.faixaDeCep('05500000', '05599999'), '05500-000 a 05599-999')
})

// ── Segunda rodada (K1 a K9) ────────────────────────────────────────────────
const abertura = await import('../src/dominio/aberturaDaLoja.js')
const cozinhaApi = await import('../src/aplicacao/useCozinhaApi.js')

const DIA_ESQUECIDO = { ...DIA, aberto: true, esquecidoAberto: true, abertoDesde: '2026-10-07' }
await confere('K1 caixa esquecido confere a gaveta (só dinheiro), não o total com Pix', () => {
  const s = abertura.situacaoDoCaixaParaAbrirLoja(DIA_ESQUECIDO, null)
  assert.equal(s.tipo, 'esquecido')
  assert.equal(s.naGaveta, 130.5)
  const aberto = abertura.situacaoDoCaixaParaAbrirLoja({ ...DIA, aberto: true }, null)
  assert.equal(aberto.naGaveta, 130.5)
})
await confere('K2 conferência começa sem contagem e diz faltam, sobram ou bate certo', () => {
  assert.equal(caixa.conferenciaDaGaveta(null, 100).pronta, false)
  assert.equal(caixa.conferenciaDaGaveta(null, 100).texto, null)
  const falta = caixa.conferenciaDaGaveta(9500, 100)
  assert.equal(falta.pronta, true)
  assert.match(falta.texto, /^Faltam R\$\s5,00$/)
  assert.match(caixa.conferenciaDaGaveta(10500, 100).texto, /^Sobram R\$\s5,00$/)
  assert.equal(caixa.conferenciaDaGaveta(10000, 100).texto, 'Bate certo')
  assert.equal(caixa.conferenciaDaGaveta(10000, 100).tom, 'ok')
  assert.equal(caixa.conferenciaDaGaveta(2000, 100).tom, 'perigo')
})
await confere('K3 suprimento é entrada (reforço de troco) e soma na gaveta', () => {
  assert.deepEqual(caixa.CATEGORIAS_SAIDA_SUGERIDAS, ['Sangria', 'Despesa'])
  assert.ok(caixa.CATEGORIAS_ENTRADA_SUGERIDAS.includes('Suprimento'))
  const dia = { saldoInicial: 50, movimentos: [{ tipo: 'entrada', categoria: 'Suprimento', valor: 20, meio: 'dinheiro' }], linhasExtras: [] }
  assert.equal(caixa.gavetaDoDia(dia).naGaveta, 70)
})
await confere('K5 resposta velha da fila (Hoje) não sobrescreve a nova (Amanhã)', () => {
  const g = cozinhaApi.criarGeracao()
  const hoje = g.nova()
  const amanha = g.nova()
  assert.equal(g.vale(hoje), false)
  assert.equal(g.vale(amanha), true)
})
await confere('K7 aviso da automática de entrada aponta a mensagem certa por gatilho', () => {
  const desligada = (gatilho) => automacao.avisoDaAutomaticaDeEntrada({ gatilho, ativa: false, texto: '' })
  assert.match(desligada(automacao.GATILHOS.PRIMEIRO_CONTATO), /saudação/)
  assert.match(desligada(automacao.GATILHOS.FORA_DO_HORARIO), /Horários e mensagens › Fora do horário/)
  assert.doesNotMatch(desligada(automacao.GATILHOS.FORA_DO_HORARIO), /saudação/)
  assert.match(desligada(automacao.GATILHOS.LOJA_FECHADA), /Horários e mensagens › Loja fechada/)
  assert.match(automacao.avisoDaAutomaticaDeEntrada({ gatilho: automacao.GATILHOS.LOJA_FECHADA, ativa: true, texto: 'Oi' }), /duas mensagens/)
})
await confere('K7 guarda de variável bloqueia só as do sistema, com acento e espaço', () => {
  assert.deepEqual(respostas.variaveisNaoResolvidas('Use {CUPOM10} no pedido {1}.'), [])
  assert.deepEqual(respostas.variaveisNaoResolvidas('Oi { nome }, seu {Pedido} e {endereço}.'), ['{ nome }', '{Pedido}', '{endereço}'])
  assert.equal(respostas.avisoDeVariaveisNaoResolvidas('Pedido {pedido}.'),
    'Esta conversa não tem número do pedido: apague {pedido} ou escreva no lugar.')
  assert.equal(respostas.avisoDeVariaveisNaoResolvidas('Cupom {CUPOM10}'), null)
})
await confere('K7 data do bloqueio "seg, 12/10"', () => {
  assert.equal(formato.dataCurtaComSemana('2026-10-12'), 'seg, 12/10')
  assert.equal(formato.dataCurtaComSemana('2026-10-11'), 'dom, 11/10')
})
await confere('K7 sem jargão interno (Fnn, plano do ERP) em texto visível', () => {
  for (const arquivo of ['../src/features/gestao/ModalGestao.jsx', '../src/features/hall/MolduraDoModulo.jsx']) {
    const fonte = readFileSync(new URL(arquivo, import.meta.url), 'utf8')
      .split('\n').filter((l) => !/^\s*(\/\/|\*|\{\/\*)/.test(l)).join('\n')
    assert.doesNotMatch(fonte, /\(F\d+( em diante)?\)|plano do ERP/, arquivo)
  }
})
await confere('K9 aviso persistente (loja online desligada) sobrevive à limpeza automática', () => {
  let e = reducer({ sincronizacao: {} }, { tipo: acao.AVISO_API, mensagem: 'A loja online está desligada', persistente: true })
  e = reducer(e, { tipo: acao.AVISO_API, mensagem: 'Janela lotada.' })
  e = reducer(e, { tipo: acao.FECHAR_AVISO_API })
  assert.equal(e.sincronizacao.aviso, null)
  assert.equal(e.sincronizacao.avisoFixo, 'A loja online está desligada')
  e = reducer(e, { tipo: acao.FECHAR_AVISO_API, fixo: true })
  assert.equal(e.sincronizacao.avisoFixo, null)
})

await confere('pagamento e entrega: automática desligada não diz que nada sai (o aviso do pedido sai)', async () => {
  const { avisoDaAutomaticaDeEntrada: aviso, GATILHOS: G } = await import('../src/dominio/automacao.js')
  assert.match(aviso({ gatilho: G.PAGAMENTO_CONFIRMADO, ativa: false }), /já recebe o aviso de pagamento confirmado/)
  assert.match(aviso({ gatilho: G.POS_ENTREGA, ativa: true, texto: 'Obrigada!' }), /duas mensagens/)
  assert.equal(aviso({ gatilho: G.ENCERRAMENTO, ativa: false }), null)
})

if (falhas.length > 0) {
  console.error(`\n${passou} ok, ${falhas.length} falha(s)`)
  process.exit(1)
}
console.log(`\n${passou} ok, 0 falha(s)`)
