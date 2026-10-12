using EasyStock.Domain.Entities.Pagamentos;

namespace EasyStock.Application.Ports.Output.Persistence.Pagamentos;

/// <summary>Cobrança pendente cujo link já venceu, na varredura cross-tenant do job (S11).</summary>
public sealed record CobrancaPedidoVencida(Guid CobrancaId, Guid EmpresaId, Guid PedidoId);

/// <summary>
/// Persistência de <see cref="CobrancaPedido"/> (S11). Não faz SaveChanges: o commit é do
/// <see cref="IUnitOfWork"/>. <c>empresaId</c> vai no WHERE de toda consulta de tenant (ADR-0010).
/// </summary>
public interface ICobrancaPedidoRepository
{
    Task AddAsync(CobrancaPedido cobranca, CancellationToken ct = default);

    /// <summary>Todas as cobranças do pedido, rastreadas, da mais antiga para a mais nova.</summary>
    Task<IReadOnlyList<CobrancaPedido>> ListarDoPedidoAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default);

    /// <summary>
    /// Tenant do pedido a partir das cobranças, sem contexto de tenant (webhook do Mercado Pago, S32):
    /// o <c>external_reference</c> traz só o <c>PedidoId</c>. Null quando o pedido não tem cobrança.
    /// </summary>
    Task<Guid?> ObterEmpresaIdDoPedidoAsync(Guid pedidoId, CancellationToken ct = default);

    /// <summary>
    /// Cobranças online pendentes com <c>ExpiraEm &lt;= limite</c>, de todos os tenants (job S11),
    /// das que venceram antes primeiro.
    /// </summary>
    Task<IReadOnlyList<CobrancaPedidoVencida>> ListarPendentesVencidasAsync(DateTime limite, int maximo, CancellationToken ct = default);

    /// <summary>
    /// Há alguma cobrança online pendente, em qualquer loja (I3.1)? O webhook que não consegue reconsultar o
    /// pagamento usa isto para decidir entre pedir reenvio e ignorar. Só devolve sim ou não.
    /// </summary>
    Task<bool> ExistePendenteOnlineAsync(CancellationToken ct = default);
}
