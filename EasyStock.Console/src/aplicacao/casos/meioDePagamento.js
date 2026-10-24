import {
  cobrancaTrocada, criarCobranca, estadoDepoisDaTroca, nomeDoMeio, podeAlterarMeio, textoDaCobranca,
} from '../../dominio/cobranca'
import { indiceDoPasso } from '../../dominio/esteira'
import * as acao from '../acoes'
import { comMensagem, mapear } from '../reducer/auxiliares'
import { ATENDENTE } from '../reducer/estadoInicial'

// Meio de pagamento, troco e andamento da esteira sem link. O modo API também despacha estes casos
// (`aplicacao/api/comanda.js` e as ações locais da comanda em rascunho).

export const casosMeioDePagamento = {
  // "Gerar a cobrança da maquininha ou do vale" ANDA a esteira sem esperar
  // pagamento (seção 4, decisão 19: "A esteira anda sem pagamento";
  // `liberaEsteiraSemPagar` de `infra/catalogo.js`). Quem dispara esta ação é
  // a própria emissão da cobrança (ver `aplicacao/AtendimentoProvider.jsx`,
  // `gerarCobranca`/`reenviarCobranca`): ao ver que a emissão voltou sem
  // `link`, despacha GERAR_COBRANCA e, na sequência, esta.
  //
  // Nunca anda para trás e nunca pula "pago" de um pedido que já foi além
  // dele (mesma guarda de `comPagamentoReconhecido` em reducer.js).
  [acao.DESPACHAR_MESMO_ASSIM]: (estado, { id }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    const pedido = conversa?.pedido
    if (!pedido || indiceDoPasso(pedido.estado) >= indiceDoPasso('pago')) return estado
    return mapear(estado, id, (c) => ({ ...c, pedido: { ...c.pedido, estado: 'pago' } }))
  },

  // Ponta (a) da integração: a escolha do chip mora no pedido, não no estado
  // local do bloco. Assim "Enviar comanda" e o atalho "Gerar cobrança" da
  // barra mandam o MESMO meio que ela marcou; sem meio marcado, os dois
  // perguntam antes de mandar. Só antes de existir cobrança: depois disso o
  // meio é o da própria cobrança, e mudar é "Trocar meio".
  [acao.ESCOLHER_MEIO_PAGAMENTO]: (estado, { id, meio }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido || conversa.pedido.cobranca) return estado
    return mapear(estado, id, (c) => ({ ...c, pedido: { ...c.pedido, meio } }))
  },

  // J2 (#1990): o troco mora no pedido, como o meio: vai no POST do pedido (observações) pela
  // barra, pelo bloco Cobrança ou pela comanda, sem cada tela guardar o seu. Depois que o pedido
  // existe no EasyStok, o que vale é o que ficou gravado nele.
  [acao.DEFINIR_TROCO]: (estado, { id, trocoPara }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido || conversa.pedido.pedidoId) return estado
    return mapear(estado, id, (c) => ({ ...c, pedido: { ...c.pedido, trocoPara } }))
  },

  // Rodada 12 (issue #13): "Alterar forma de pagamento". Numa ação só: a
  // cobrança pendente vai para o histórico, cancelada; sai uma nova na forma
  // escolhida, com texto que conta a troca ao cliente; a conversa ganha o
  // registro de sistema. O pedido é o mesmo (número, itens, janela). A emissão
  // chega pronta do Provider, como em GERAR_COBRANCA.
  [acao.ALTERAR_MEIO_PAGAMENTO]: (estado, {
    id, agora, emissao, mensagemId, sistemaId,
  }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    const pedido = conversa?.pedido
    if (!podeAlterarMeio(pedido) || emissao?.meio === pedido.cobranca.meio) return estado
    const anterior = pedido.cobranca
    const base = { ...pedido, estado: estadoDepoisDaTroca(pedido, anterior, emissao.link != null) }
    const cobranca = {
      ...criarCobranca(base, estado.catalogo.cardapio, agora, emissao),
      tentativa: (anterior.tentativa ?? 1) + 1,
      meioAnterior: anterior.meio ?? 'pix',
    }
    const novoPedido = {
      ...base,
      meio: cobranca.meio,
      cobranca,
      cobrancasAnteriores: [...(pedido.cobrancasAnteriores ?? []), cobrancaTrocada(anterior, agora)],
    }
    const registro = `Forma de pagamento alterada de ${nomeDoMeio(anterior.meio)} para `
      + `${nomeDoMeio(cobranca.meio)} por ${ATENDENTE}. A cobrança anterior foi cancelada.`
    return mapear(estado, id, (c) => comMensagem(
      comMensagem({ ...c, pedido: novoPedido }, { dir: 'sistema', texto: registro }, { id: sistemaId, agora }),
      {
        dir: 'out', texto: textoDaCobranca(cobranca, novoPedido, estado.catalogo.cardapio),
        status: 'lida', automatica: true, regra: 'cobranca-pix',
      },
      { id: mensagemId, agora },
    ))
  },
}
