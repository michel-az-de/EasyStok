namespace EasyStock.Application.Ports.Output.Persistence.Pagamentos;

public sealed record RecebimentoOnline(Guid PagamentoId, Guid CobrancaId, string PagamentoExternoId,
    decimal Valor, string Metodo, DateTime PagoEm, Guid? LojaId, bool Estornada);

public interface IEstornoOnlineRepository
{
    Task<IReadOnlyList<RecebimentoOnline>> RecebimentosAsync(Guid empresaId, Guid pedidoId, CancellationToken ct);
    Task<IReadOnlyList<PedidoEstornoOnline>> ListarAsync(Guid empresaId, Guid pedidoId, CancellationToken ct);
    Task AdicionarAsync(PedidoEstornoOnline estorno, CancellationToken ct);
    Task AtualizarAsync(PedidoEstornoOnline estorno, CancellationToken ct);
    Task MarcarCobrancaEstornadaAsync(Guid empresaId, Guid cobrancaId, DateTime agora, CancellationToken ct);
}
