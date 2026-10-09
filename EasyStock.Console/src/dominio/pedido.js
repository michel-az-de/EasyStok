import { itemPorSku } from './cardapio.js'

export function itensDetalhados(pedido, cardapio) {
  if (!pedido) return []
  return pedido.itens.map((linha) => ({ ...linha, produto: itemPorSku(cardapio, linha.sku) }))
}

export function totalDoPedido(pedido, cardapio) {
  return itensDetalhados(pedido, cardapio)
    .reduce((soma, linha) => soma + (linha.produto?.preco ?? 0) * linha.qtd, 0)
}

// `diferencaAposPagamento` mudou para `dominio/pagamento.js` na integração:
// "quanto já foi pago" tem uma fonte só (registro 38).

// Pedido novo nasce aguardando pagamento, nunca pago. O número vem de fora
// para o domínio não depender de contador global. `pagamentos` (rodada 5,
// seção 3.2 e 4) é a soma de tudo que já caiu no pedido: a cobrança original
// e qualquer complemento, lida por `dominio/pagamento.js`.
//
// `entregador` nasce `null`: US-040, ninguém foi escalado ainda, e um texto
// fixo aqui vira mentira assim que o pedido despachar sem a dona escolher
// quem leva (`dominio/viagem.js: entregadorResolvido`, `dominio/esteira.js:
// avisoDoPasso`).
//
// `janela` também nasce `null` (#1474): o 'j3' fixo virava "Janela 18h30 às 19h30" na comanda
// sem ninguém ter escolhido janela nenhuma.
export const novoPedido = (numero) => ({
  numero, estado: 'aguardando', janela: null, entregador: null, itens: [],
  agradecimentoEnviado: false, pagamentos: [],
})

// Item repetido soma quantidade em vez de virar segunda linha, MAS só quando
// a anotação é igual e os dois são (ou não são) acréscimo: mesmo prato com
// anotação diferente vira linha própria (seção 3.3, "Dois tortéis, um sem
// tempero" é o caso real). `acrescimo`/`entrouEm` marcam item que entrou na
// comanda depois do pagamento (seção 3.2); item normal nasce com os dois
// vazios.
export function comItem(pedido, sku, { obs = '', acrescimo = false, entrouEm = null } = {}) {
  const existente = pedido.itens.find(
    (linha) => linha.sku === sku && linha.obs === obs && Boolean(linha.acrescimo) === acrescimo,
  )
  if (existente) {
    return {
      ...pedido,
      itens: pedido.itens.map((l) => (l === existente ? { ...l, qtd: l.qtd + 1 } : l)),
    }
  }
  return { ...pedido, itens: [...pedido.itens, { sku, qtd: 1, obs, acrescimo, entrouEm }] }
}

export function semItem(pedido, sku) {
  return { ...pedido, itens: pedido.itens.filter((linha) => linha.sku !== sku) }
}

// Observação mora na linha do item, nunca no rodapé do pedido (RN-20): é o
// "sem queijo" que a cozinha precisa ler junto do prato, não depois dele.
export function comObservacao(pedido, sku, texto) {
  return {
    ...pedido,
    itens: pedido.itens.map((linha) => (linha.sku === sku ? { ...linha, obs: texto } : linha)),
  }
}

// Cancelado encerra igual a entregue: nenhum dos dois edita comanda nem anda
// na esteira (secao 5 da direção visual, "Cancelado" fica fora da trilha).
export const pedidoEncerrado = (pedido) =>
  pedido?.estado === 'entregue' || pedido?.estado === 'cancelado'

// Número curto de quatro dígitos, o mesmo formato que a comanda e o canhoto
// usam de relance (ex.: "2026-0186" vira "0186").
export const numeroCurto = (numero) => String(numero).split('-').at(-1)

// #1474 (R6): no modo API o número é o do EasyStok (8 letras, o mesmo da Cozinha e das
// Entregas). Antes de o pedido existir não há número: o "0001" local seria inventado.
export function numeroDaComanda(pedido, { fonteApi = false } = {}) {
  if (pedido?.pedidoId) return numeroCurto(pedido.numero)
  return fonteApi ? null : numeroCurto(pedido?.numero ?? '')
}

// Ordem fixa da linha de produção na cozinha: quem prepara na hora primeiro,
// quem é para levar e finalizar em casa depois (QA2-19, conteúdo do canhoto).
// A comanda em tela e o canhoto impresso agrupam pela mesma ordem.
export const ORDEM_DAS_LINHAS = ['servir', 'casa']

export function agruparPorLinha(itens, linhas) {
  return ORDEM_DAS_LINHAS
    .map((chave) => ({
      chave,
      rotulo: linhas[chave]?.rotulo ?? chave,
      itens: itens.filter((l) => l.produto?.linha === chave),
    }))
    .filter((grupo) => grupo.itens.length > 0)
}
