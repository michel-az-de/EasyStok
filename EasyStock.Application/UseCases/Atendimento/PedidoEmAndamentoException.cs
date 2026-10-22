namespace EasyStock.Application.UseCases.Atendimento;

/// <summary>
/// A conversa já tem um pedido não finalizado (#1238): o clique duplo ou a corrida perdeu. A API devolve
/// 409 <c>PEDIDO_EM_ANDAMENTO</c> e o console relê a conversa, porque o pedido existe (#1942).
/// </summary>
public sealed class PedidoEmAndamentoException()
    : RegraDeDominioVioladaException(CriarPedidoAtendimentoUseCase.PedidoEmAndamento);
