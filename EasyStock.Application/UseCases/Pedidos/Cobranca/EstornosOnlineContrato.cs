using EasyStock.Application.Ports.Output.Pagamentos;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

public sealed record SolicitarEstornoOnlineInput(Guid EmpresaId, Guid PedidoId, Guid OperacaoId, Guid PagamentoId,
    decimal Valor, string Motivo, Guid UsuarioId, string? UsuarioNome, NivelAcesso NivelSolicitante);
public sealed record RecebimentoParaEstornoOnline(Guid Id, string Metodo, decimal Valor, DateTime PagoEm,
    decimal Devolvido, decimal Reservado, decimal Disponivel, bool Legado);
public sealed record EstornosOnlineResult(IReadOnlyList<RecebimentoParaEstornoOnline> Pagamentos, IReadOnlyList<PedidoEstornoOnline> Estornos);

public interface IEstornosOnlineService
{
    Task<EstornosOnlineResult> ConsultarAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default);
    Task<PedidoEstornoOnline> SolicitarAsync(SolicitarEstornoOnlineInput input, CancellationToken ct = default);
    Task<PedidoEstornoOnline> RetomarAsync(Guid empresaId, Guid pedidoId, Guid operacaoId, NivelAcesso nivel, CancellationToken ct = default);
    Task<bool> SincronizarPagamentoAsync(PagamentoMercadoPago pagamento, CancellationToken ct = default);
}
