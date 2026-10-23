using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

public interface ICreditoClienteComandaRepository
{
    Task AddAsync(CreditoClienteComanda credito, CancellationToken ct);
    Task AdicionarUsoAsync(UsoCreditoClienteComanda uso, CancellationToken ct);
    Task<CreditoClienteComanda?> ObterPorOperacaoAsync(Guid empresaId, Guid operacaoId, CancellationToken ct);
    Task<CreditoClienteComanda?> ObterPorCodigoAsync(Guid empresaId, Guid clienteId, string codigo, CancellationToken ct);
    Task<CreditoClienteComanda?> ObterComLockAsync(Guid empresaId, Guid clienteId, string codigo, CancellationToken ct);
    Task<IReadOnlyList<CreditoClienteComanda>> ListarAsync(Guid empresaId, Guid clienteId, CancellationToken ct);
    Task<(decimal Emitido, decimal Utilizado)> TotaisDoPedidoAsync(Guid empresaId, Guid pedidoId, CancellationToken ct);

    /// <summary>Usos rastreados do pedido, para devolver ao cupom quando o pedido é desfeito sem cobrança (#1961).</summary>
    Task<IReadOnlyList<UsoCreditoClienteComanda>> ListarUsosDoPedidoAsync(Guid empresaId, Guid pedidoId, CancellationToken ct);
    Task<CreditoClienteComanda?> ObterComLockPorIdAsync(Guid empresaId, Guid creditoId, CancellationToken ct);
    void RemoverUso(UsoCreditoClienteComanda uso);
}
