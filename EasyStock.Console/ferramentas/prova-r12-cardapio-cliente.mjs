// Prova da issue #19 (rodada 12, registro 98): cardápio que o cliente marca,
// estilo iFood, com o pedido voltando pronto para a conversa.
// Feedback da Thatiane (áudio de 26/09/2026): "em vez de mandar só um link
// simples, uma página com mais imagens, onde o cliente marca os itens e a
// quantidade e o pedido volta pronto, sem eu digitar".
//
// Duas partes:
//   1. domínio puro (`dominio/cardapioLink.js`, `dominio/arteCardapio.js`):
//      vitrine com indisponível visível e desabilitado (RN-16), busca sem
//      acento, categoria, observação na linha do item (RN-20), foto por prato
//      e o link do convite clicável;
//   2. reducer de verdade (decisão revista no registro 98: a página continua
//      cobrando): "Enviar pedido" monta a comanda na conversa já com a
//      cobrança no meio escolhido; Pix e cartão pagam na página e a baixa
//      chega pelo CONFIRMAR_PAGAMENTO; maquininha e vale andam a esteira; a
//      conversa só sobe para "Precisa de você" quando há observação.
//
// Mesmo gancho de `prova-r11-atendimento.mjs`: o código de `src/` importa sem
// extensão (o Vite resolve, o Node puro não).
//
// Roda com: node ferramentas/prova-r12-cardapio-cliente.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const acao = await import('../src/aplicacao/acoes.js')
const link = await import('../src/dominio/cardapioLink.js')
const { fotoDoPrato } = await import('../src/dominio/arteCardapio.js')
const { precisaDeVoce } = await import('../src/dominio/automatico.js')
const { REGRAS_PADRAO } = await import('../src/dominio/automacao.js')
const {
  CARDAPIO, CANAIS, JANELAS_ENTREGA, LINHAS_PRODUTO, MEIOS_DE_PAGAMENTO, PREFIXOS_CEP_ATENDIDOS,
} = await import('../src/infra/catalogo.js')
const { emitirCobranca } = await import('../src/infra/provedoresDeCobranca.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }
const semTravessao = (texto) => !/[–—]/.test(texto)

// Cardápio do dia com os três casos de indisponível lado a lado: saldo zero
// (LAS-VER já nasce assim), desligado do dia pela dona, tirado do cardápio e
// em validação (RN-15: o site é venda automática, não oferece).
const cardapioDoDia = CARDAPIO.map((item) => {
  if (item.sku === 'TOR-COS') return { ...item, disponivelHoje: false }
  if (item.sku === 'EXT-PAR') return { ...item, removidoEm: '2026-09-25T10:00:00-03:00' }
  return item
}).concat({ sku: 'GNO-BAT', nome: 'Nhoque de batata', linha: 'casa', porcao: '500 g', preco: 58, estoque: 5, emValidacao: true })

const skusDe = (grupos) => grupos.flatMap((g) => g.itens.map((i) => i.sku))

// --- 1. Domínio -------------------------------------------------------------------
confere('vitrine mostra esgotado e fora do dia, desabilitados com o motivo (RN-16)', () => {
  const grupos = link.vitrineDoCardapio(cardapioDoDia, LINHAS_PRODUTO)
  const itens = grupos.flatMap((g) => g.itens)
  const porSku = (sku) => itens.find((i) => i.sku === sku)
  assert.equal(porSku('LAS-VER').motivoIndisponivel, 'Esgotado hoje')
  assert.equal(porSku('TOR-COS').motivoIndisponivel, 'Fora do cardápio de hoje')
  assert.equal(porSku('LAS-CLA').motivoIndisponivel, null)
})
confere('vitrine não mostra item tirado do cardápio nem em validação (RN-15)', () => {
  const skus = skusDe(link.vitrineDoCardapio(cardapioDoDia, LINHAS_PRODUTO))
  assert.ok(!skus.includes('EXT-PAR'))
  assert.ok(!skus.includes('GNO-BAT'))
})
confere('vitrine agrupa por categoria na ordem da comanda, com rótulo e dica', () => {
  const grupos = link.vitrineDoCardapio(cardapioDoDia, LINHAS_PRODUTO)
  assert.deepEqual(grupos.map((g) => g.rotulo), ['Para servir', 'Preparar em casa'])
  assert.equal(grupos[0].dica, LINHAS_PRODUTO.servir.dica)
  // Disponível primeiro dentro da categoria: indisponível não fica no topo.
  assert.equal(grupos[0].itens.at(-1).sku, 'LAS-VER')
})
confere('busca ignora acento e caixa; vazia devolve tudo', () => {
  const grupos = link.vitrineDoCardapio(cardapioDoDia, LINHAS_PRODUTO)
  assert.deepEqual(skusDe(link.filtrarVitrine(grupos, { busca: 'RAVIOLI' })), ['RAV-LIM', 'RAV-ABO'])
  assert.deepEqual(skusDe(link.filtrarVitrine(grupos, { busca: 'abobora' })), ['RAV-ABO'])
  assert.deepEqual(skusDe(link.filtrarVitrine(grupos, { busca: '  ' })), skusDe(grupos))
  assert.deepEqual(link.filtrarVitrine(grupos, { busca: 'pizza' }), [])
})
confere('categoria filtra a vitrine; "todas" não filtra', () => {
  const grupos = link.vitrineDoCardapio(cardapioDoDia, LINHAS_PRODUTO)
  assert.deepEqual(link.filtrarVitrine(grupos, { categoria: 'casa' }).map((g) => g.chave), ['casa'])
  assert.equal(link.filtrarVitrine(grupos, { categoria: link.TODAS_AS_CATEGORIAS }).length, 2)
})
confere('observação mora na linha do item (RN-20) e o "+" soma na mesma linha', () => {
  let carrinho = link.carrinhoComItem(link.carrinhoVazio(), 'RAV-LIM')
  carrinho = link.carrinhoComObservacao(carrinho, 'RAV-LIM', 'sem manteiga')
  carrinho = link.carrinhoComItem(carrinho, 'RAV-LIM')
  assert.deepEqual(carrinho.itens, [{ sku: 'RAV-LIM', qtd: 2, obs: 'sem manteiga', acrescimo: false, entrouEm: null }])
})
confere('quantidade e subtotal do carrinho', () => {
  let carrinho = link.carrinhoComItem(link.carrinhoVazio(), 'LAS-CLA')
  carrinho = link.carrinhoComQuantidade(carrinho, 'LAS-CLA', 1)
  carrinho = link.carrinhoComItem(carrinho, 'RAV-LIM')
  assert.equal(link.quantidadeNoCarrinho(carrinho), 3)
  assert.equal(link.subtotalDoCarrinho(carrinho, CARDAPIO), 85 * 2 + 62)
})
confere('pagamento: Pix e cartão em destaque, maquininha e vale "Na entrega" (PR #18)', () => {
  const { principais, naEntrega } = link.meiosDaPagina(MEIOS_DE_PAGAMENTO)
  assert.deepEqual(principais.map((m) => m.id), ['pix', 'cartao-link'])
  assert.deepEqual(naEntrega.map((m) => m.id), ['maquininha', 'vale-refeicao'])
  assert.deepEqual(link.meiosDaPagina([{ id: 'x', situacao: 'ativo', provedor: 'manual', destaque: true }]).principais.map((m) => m.id), ['x'])
})
confere('foto do prato: imagem própria por prato, sem texto dentro', () => {
  const fotos = CARDAPIO.map(fotoDoPrato)
  for (const foto of fotos) {
    assert.ok(foto.startsWith('data:image/svg+xml'))
    assert.ok(!decodeURIComponent(foto).includes('<text'), 'nome e preço vão no cartão, não na foto')
  }
  assert.equal(new Set(fotos).size, fotos.length, 'cada prato com a sua')
})
confere('link do convite vira trecho clicável na bolha; texto sem link fica inteiro', () => {
  const url = link.linkDoCardapio('http://127.0.0.1:5173/', 'c7')
  const partes = link.partesComLinkDoCardapio(link.textoConviteCardapio('Norma Lima', url))
  assert.equal(partes.length, 2)
  assert.equal(partes[1].tipo, 'link')
  assert.equal(partes[1].texto, url)
  assert.deepEqual(link.partesComLinkDoCardapio('Oi, tudo bem?'), [{ tipo: 'texto', texto: 'Oi, tudo bem?' }])
})

// --- 2. Reducer: "Enviar pedido" até a comanda da operadora ------------------------
const AGORA = Date.parse('2026-09-26T11:05:00-03:00')
const catalogo = {
  cardapio: cardapioDoDia, canais: CANAIS, janelas: JANELAS_ENTREGA, linhas: LINHAS_PRODUTO,
  meiosDePagamento: MEIOS_DE_PAGAMENTO, prefixosCepAtendidos: PREFIXOS_CEP_ATENDIDOS,
}
const clienteDoWhatsApp = {
  id: 'c-norma', cadastroId: 'c-norma', conta: 'cliente', nome: 'Norma Lima', canal: 'WhatsApp', estado: 'Em atendimento',
  responsavel: null, atrasada: false, bloqueio: null, pedido: null,
  mensagens: [{ id: 'm0', dir: 'out', texto: 'Segue o cardápio', em: new Date(AGORA - 60000).toISOString(), status: 'lida' }],
  ultimaEm: new Date(AGORA - 60000).toISOString(),
  cliente: { desde: '2025', endereco: null, telefone: '(11) 90000-0000', pedidos: 3, tags: [], notas: [] },
}
let estado = estadoInicial({
  conversas: [clienteDoWhatsApp], catalogo, regras: REGRAS_PADRAO, modoAgente: 'sugerir', lojaAberta: true,
})
const conversa = () => estado.conversas.find((c) => c.id === 'c-norma')

let carrinho = link.carrinhoComItem(link.carrinhoVazio(), 'LAS-CLA')
carrinho = link.carrinhoComQuantidade(carrinho, 'LAS-CLA', 1)
carrinho = link.carrinhoComItem(carrinho, 'RAV-LIM')
carrinho = link.carrinhoComObservacao(carrinho, 'RAV-LIM', 'sem manteiga')
const ENDERECO = 'Rua Girassol, 412, Vila Madalena, CEP 05433-001'

confere('"Enviar pedido" só libera com itens, horário, endereço na área e pagamento', () => {
  const base = {
    carrinho, janelaId: 'j3', endereco: ENDERECO, meio: 'maquininha', prefixosCepAtendidos: PREFIXOS_CEP_ATENDIDOS,
  }
  assert.equal(link.pedidoLinkPodeSerConfirmado(base), true)
  assert.equal(link.pedidoLinkPodeSerConfirmado({ ...base, carrinho: link.carrinhoVazio() }), false)
  assert.equal(link.pedidoLinkPodeSerConfirmado({ ...base, endereco: 'Rua sem CEP, 10' }), false)
})

const emitir = (meio, numero, valor) => ({ ...emitirCobranca(meio, { numeroPedido: numero, valor }), meio })
const enviarPedido = (id, numero, itens, meio) => {
  estado = reducer(estado, {
    tipo: acao.CRIAR_PEDIDO_CARDAPIO_LINK, id, agora: AGORA, mensagemId: `site-${numero}`, numero,
    itens, janela: 'j3', endereco: ENDERECO, meio, emissao: emitir(meio, numero, 232),
  })
  // O mesmo que `acoes/cardapioLink.js` faz depois: sem link, a esteira anda.
  if (!emitir(meio, numero, 0).link) estado = reducer(estado, { tipo: acao.DESPACHAR_MESMO_ASSIM, id, agora: AGORA })
}

const saldoAntes = estado.catalogo.cardapio.find((i) => i.sku === 'LAS-CLA').estoque
enviarPedido('c-norma', '2026-0301', carrinho.itens, 'pix')

confere('reducer: a comanda chega preenchida, com observação, horário, endereço e meio', () => {
  const { pedido, cliente } = conversa()
  assert.equal(pedido.numero, '2026-0301')
  assert.equal(pedido.estado, 'aguardando')
  assert.deepEqual(pedido.itens.map((l) => [l.sku, l.qtd, l.obs]), [['LAS-CLA', 2, ''], ['RAV-LIM', 1, 'sem manteiga']])
  assert.equal(pedido.janela, 'j3')
  assert.equal(pedido.meio, 'pix')
  assert.equal(cliente.endereco, ENDERECO)
})
confere('reducer: Pix sai junto com o pedido, pendente (RN-23: o pedido nasce antes do pagamento)', () => {
  const { cobranca } = conversa().pedido
  assert.equal(cobranca.meio, 'pix')
  assert.equal(cobranca.valor, 232)
  assert.equal(cobranca.pagaEm, null)
  assert.ok(cobranca.copiaECola)
})
confere('reducer: saldo baixa pelo que o cliente marcou', () => {
  assert.equal(estado.catalogo.cardapio.find((i) => i.sku === 'LAS-CLA').estoque, saldoAntes - 2)
})
confere('reducer: a conversa recebe o pedido montado na fala do cliente e o cartão com a situação', () => {
  const [eco, cartao] = conversa().mensagens.slice(-2)
  assert.equal(eco.dir, 'in')
  assert.match(eco.texto, /2× Lasanha clássica, 1× Ravióli de limão siciliano \(sem manteiga\)/)
  assert.equal(cartao.dir, 'sistema')
  assert.match(cartao.texto, /Pedido 2026-0301 chegou pelo cardápio online/)
  assert.match(cartao.texto, /Aguardando pagamento no Pix\. Tem observação: confira a comanda\./)
  assert.ok(semTravessao(eco.texto) && semTravessao(cartao.texto))
})
confere('reducer: com observação, a conversa sobe em "Precisa de você" para conferir', () => {
  assert.equal(conversa().passagem.motivo, link.MOTIVO_CONFERIR_PEDIDO_LINK)
  assert.equal(precisaDeVoce(conversa(), AGORA, false, true, JANELAS_ENTREGA), true)
})
confere('reducer: "Simular pagamento" da página dá baixa pelo CONFIRMAR_PAGAMENTO de sempre', () => {
  estado = reducer(estado, { tipo: acao.CONFIRMAR_PAGAMENTO, id: 'c-norma', agora: AGORA + 60000, mensagemId: 'pg1' })
  assert.ok(conversa().pedido.cobranca.pagaEm)
  assert.equal(conversa().pedido.estado, 'pago')
})

const sergio = { ...clienteDoWhatsApp, id: 'c-sergio', cadastroId: 'c-sergio', nome: 'Sérgio Prado', passagem: null }
estado = reducer(estado, { tipo: acao.SIMULAR_CONVERSA, conversa: sergio })
const conversaSergio = () => estado.conversas.find((c) => c.id === 'c-sergio')
enviarPedido('c-sergio', '2026-0302', [{ sku: 'PAP-RAG', qtd: 1, obs: '', acrescimo: false, entrouEm: null }], 'maquininha')

confere('reducer: maquininha sem observação segue direto, sem "Precisa de você"', () => {
  const c = conversaSergio()
  assert.equal(c.pedido.cobranca.meio, 'maquininha')
  assert.equal(c.pedido.cobranca.link ?? null, null)
  assert.equal(c.pedido.estado, 'pago', 'maquininha anda a esteira, igual ao Balcão')
  assert.ok(!c.passagem)
  assert.match(c.mensagens.at(-1).texto, /Pagamento na entrega, por maquininha\.$/)
})
confere('reducer: carrinho vazio não cria pedido', () => {
  const antes = estado
  estado = reducer(estado, {
    tipo: acao.CRIAR_PEDIDO_CARDAPIO_LINK, id: 'c-sergio', agora: AGORA, mensagemId: 'x', numero: '2026-0303',
    itens: [], janela: 'j3', endereco: ENDERECO, meio: 'pix', emissao: emitir('pix', '2026-0303', 0),
  })
  assert.equal(estado, antes)
})

console.log(`
${passou} verificações passaram.`)
