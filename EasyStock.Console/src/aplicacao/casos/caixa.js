// Casos de reducer da Frente Caixa (rodada 13, issue #44). Arquivo próprio:
// nenhuma outra frente edita este arquivo, e esta frente nunca edita
// `reducer.js` além da linha mínima de registro (`...casosCaixa` e o campo
// `caixa` de `estadoInicial`).
//
// Venda avulsa (UC-11, RN-26) cria uma conversa sintética de canal "Balcão"
// (fora da tabela de `infra/catalogo.js` de propósito: `canalDaConversa`
// já cai em `CANAL_DESCONHECIDO`, seguro, sem precisar editar um arquivo
// compartilhado pelas seis frentes da rodada só para isto) com um pedido que
// nasce já `pago` (ela recebeu na hora) e entra na MESMA esteira que qualquer
// outro pedido (RN-26): cozinha e entregas leem `estado.conversas` inteiro,
// sem filtrar canal.
import * as acao from '../acoes'
import {
  abrirCaixaMovimento, caixaFechamentoDoDia, criarMovimentoCaixa, estornarMovimentoCaixa,
  fecharCaixaMovimento, metodoCaixaDoMeio, nomeDoMetodoCaixa, podeAbrirCaixa, podeLancarNoCaixa, resumoCaixa,
} from '../../dominio/caixa'
import { baixarSaldo, itemPorSku, situacaoDeEstoque, textoDoAlerta } from '../../dominio/cardapio'
import { aplicarPagamento, criarCobranca } from '../../dominio/cobranca'
import { mesPorExtenso, moeda } from '../../dominio/formato'
import { comItem, novoPedido, totalDoPedido } from '../../dominio/pedido'

const ATENDENTE = 'Thatiane'

const comCaixa = (estado, movimentos) => ({ ...estado, caixa: { ...estado.caixa, movimentos } })

export const casosCaixa = {
  [acao.ABRIR_CAIXA]: (estado, { agora, saldoInicial }) => {
    const movimentos = estado.caixa?.movimentos ?? []
    if (!podeAbrirCaixa(movimentos, agora)) return estado
    return comCaixa(estado, [...movimentos, abrirCaixaMovimento(saldoInicial, agora, ATENDENTE)])
  },

  [acao.LANCAR_MOVIMENTO_CAIXA]: (estado, {
    agora, tipoMovimento, categoria, valor, meio, descricao,
  }) => {
    const movimentos = estado.caixa?.movimentos ?? []
    if (!podeLancarNoCaixa(movimentos, agora)) return estado
    const movimento = criarMovimentoCaixa({
      tipo: tipoMovimento, categoria, valor, meio, descricao, agora, autorNome: ATENDENTE,
    })
    if (!movimento) return estado
    return comCaixa(estado, [...movimentos, movimento])
  },

  [acao.ESTORNAR_MOVIMENTO_CAIXA]: (estado, { agora, movimentoId, motivo }) => {
    const movimentos = estado.caixa?.movimentos ?? []
    const alvo = movimentos.find((m) => m.id === movimentoId)
    if (!alvo) return estado
    // "Não é possível estornar movimento de dia já fechado" (mapa EasyStok):
    // o dia que importa é o do PRÓPRIO lançamento, não o de hoje.
    const fechadoNoDia = Boolean(caixaFechamentoDoDia(movimentos, alvo.criadoEm))
    const estornado = estornarMovimentoCaixa(alvo, agora, motivo, ATENDENTE, fechadoNoDia)
    if (estornado === alvo) return estado
    return comCaixa(estado, movimentos.map((m) => (m.id === movimentoId ? estornado : m)))
  },

  [acao.FECHAR_CAIXA]: (estado, { agora, contado }) => {
    const movimentos = estado.caixa?.movimentos ?? []
    if (!podeLancarNoCaixa(movimentos, agora)) return estado
    const resumo = resumoCaixa(movimentos, estado.conversas, estado.catalogo.cardapio, agora)
    return comCaixa(estado, [...movimentos, fecharCaixaMovimento(resumo, contado, agora, ATENDENTE)])
  },

  // UC-11 · Lançar venda avulsa no caixa. `itens`: [{ sku, qtd }]. `nome` é
  // texto livre (balcão sem cadastro completo, RN-26 não exige cliente).
  [acao.LANCAR_VENDA_AVULSA]: (estado, { agora, nome, itens, meio }) => {
    const movimentos = estado.caixa?.movimentos ?? []
    if (!podeLancarNoCaixa(movimentos, agora)) return estado
    const validos = (itens ?? []).filter((linha) => linha.qtd > 0 && itemPorSku(estado.catalogo.cardapio, linha.sku))
    if (validos.length === 0) return estado
    const id = `balcao-${agora}`
    if (estado.conversas.some((c) => c.id === id)) return estado

    let cardapio = estado.catalogo.cardapio
    let pedido = novoPedido(`AV-${agora}`)
    let alertas = estado.alertasEstoque
    for (const { sku, qtd } of validos) {
      for (let vez = 0; vez < qtd; vez += 1) {
        pedido = comItem(pedido, sku)
        const item = itemPorSku(cardapio, sku)
        const situacao = situacaoDeEstoque(item)
        cardapio = baixarSaldo(cardapio, sku)
        if (situacao.alerta) alertas = [...alertas, { id: `alerta-avulsa-${sku}-${agora}-${vez}`, texto: textoDoAlerta(item) }]
      }
    }

    const valor = totalDoPedido(pedido, cardapio)
    const emissao = { identificador: `cobr-balcao-${agora}`, copiaECola: null, link: null, meio }
    const cobranca = aplicarPagamento(criarCobranca(pedido, cardapio, agora, emissao), agora, null)
    const nomeCliente = (nome ?? '').trim() || 'Venda balcão'

    const conversa = {
      id,
      cadastroId: id,
      conta: 'cliente',
      nome: nomeCliente,
      canal: 'Balcão',
      estado: 'Em atendimento',
      responsavel: ATENDENTE,
      janelaExpiraEm: null,
      ultimaEm: new Date(agora).toISOString(),
      atrasada: false,
      bloqueio: null,
      cliente: {
        desde: mesPorExtenso(agora), endereco: null, enderecoCapturado: null, pedidos: 1, tags: [], notas: [],
      },
      // Retirada no balcão: sem janela de entrega (RN-26 não promete entrega
      // nenhuma, só esteira e estoque). `estado: 'pago'` porque o dinheiro já
      // está na mão dela quando ela lança a venda.
      pedido: {
        ...pedido, janela: null, estado: 'pago', cobranca, vendaAvulsa: true,
      },
      mensagens: [{
        id: `msg-${id}`,
        dir: 'sistema',
        em: new Date(agora).toISOString(),
        texto: `Venda avulsa lançada no caixa: ${moeda(valor)} por ${nomeDoMetodoCaixa(metodoCaixaDoMeio(meio))}.`,
      }],
    }

    return {
      ...estado,
      conversas: [...estado.conversas, conversa],
      catalogo: { ...estado.catalogo, cardapio },
      alertasEstoque: alertas,
    }
  },
}
