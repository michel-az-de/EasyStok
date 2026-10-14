import { pedidoEncerrado } from './pedido'

// O que a Ficha mostra recolhido quando ninguém escolheu (#1697). Medido na tela real, 1440x900,
// pedido aguardando pagamento: a comanda nascia 1,5 tela abaixo e a cobrança 3,3 telas abaixo,
// depois do cadastro, das tags e do histórico. Com pedido em andamento a Ficha é do pedido: o
// que já está resolvido recolhe e mostra o resumo ao lado do título. Cadastro incompleto fica
// aberto, porque é o que falta para cobrar. Sem pedido, a Ficha é a do cliente: tudo aberto.
export function fichaRecolhidaDePadrao({ pedido, cliente }) {
  const emPedido = Boolean(pedido) && !pedidoEncerrado(pedido)
  return {
    contato: emPedido && Boolean(cliente?.telefone && cliente?.endereco),
    tags: emPedido,
    historico: emPedido,
  }
}

// Seção que nasce recolhida não esconde erro nem o que pede ação: com erro, abre; recolhida por
// escolha de quem atende, o título diz que não carregou.
export const RESUMO_COM_ERRO = 'Não carregou'

// #1761 (L22): linha zerada da Ficha. "Tags · 0 tags" vira o convite da ação que mora ali; o
// Histórico sem pedido e sem pontos para resgatar não tem o que abrir e sai da coluna.
export const resumoDasTags = (quantas) => (quantas === 0 ? 'Adicionar tag' : quantas === 1 ? '1 tag' : `${quantas} tags`)
export const mostraHistorico = ({ pedidos = 0, fonteApi = false, resgataPontos = false } = {}) => !fonteApi || pedidos > 0 || resgataPontos

// Troca de janela de pedido criado: recolhe com a vaga já marcada (ou ainda carregando) e abre
// quando a consulta falha ou o pedido está sem vaga.
export const janelaRecolhidaDePadrao = ({ leitura, erro }) => !erro && (!leitura || Boolean(leitura.atual))

export function resumoDaJanela({ leitura, erro }) {
  if (erro) return RESUMO_COM_ERRO
  if (!leitura) return ''
  return leitura.atual?.rotulo ?? 'Sem vaga de entrega'
}

// #1709: os blocos da Ficha remontam ao trocar de conversa, de pedido ou de pagamento, mas são
// irmãos: duas chaves iguais lado a lado e o React avisa a cada render e repete seção na tela
// ("Ocorrências" aparecia duas vezes com o pedido criado). O prefixo separa os irmãos.
export function chavesDaFicha({ conversaId, pedidoId = null, pagamentos = [] }) {
  return {
    comandaCompartilhada: `comanda-compartilhada:${conversaId}`,
    cobranca: `cobranca:${conversaId}`,
    ocorrencia: `ocorrencia:${conversaId}`,
    devolucoes: `devolucoes:${pedidoId}:${pagamentos.join(':')}`,
    ocorrenciasApi: `ocorrencias-api:${pedidoId}`,
    frete: `frete:${pedidoId}`,
  }
}
