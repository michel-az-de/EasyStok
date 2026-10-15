namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Leitura do que o início previsto do pedido precisa (S21): a janela da vaga ativa, o tempo de preparo
/// dos itens e a configuração de prazo da empresa. Só leitura, <c>EmpresaId</c> no WHERE (ADR-0010).
/// </summary>
public interface IPrazoPreparoPedidoQueries
{
    /// <summary>Nulo quando o pedido não existe na empresa.</summary>
    Task<PrazoPreparoPedidoLeitura?> ObterAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default);
}

/// <param name="DataJanela">Data da vaga ativa (<c>VagaOcupada.DataEntrega</c>); nula sem vaga.</param>
/// <param name="HoraInicioJanela">Início da janela da vaga (<c>JanelaEntrega.HoraInicio</c>), hora de Brasília.</param>
/// <param name="TemposPreparoMinutos">Um por item do cardápio: o tempo próprio ou nulo (usa o padrão).
/// Pedido sem item do cardápio vem com um único nulo (vale o padrão).</param>
/// <param name="TempoPreparoPadraoMinutos"><c>ConfiguracaoAtendimento.TempoPreparoPadraoMinutos</c>.</param>
/// <param name="RespiroMinutos"><c>ConfiguracaoAtendimento.RespiroMinutos</c>.</param>
public sealed record PrazoPreparoPedidoLeitura(
    DateOnly? DataJanela,
    TimeOnly? HoraInicioJanela,
    IReadOnlyList<int?> TemposPreparoMinutos,
    int TempoPreparoPadraoMinutos,
    int RespiroMinutos);
