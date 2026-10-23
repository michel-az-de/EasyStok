using EasyStock.Domain.Entities.Pagamentos;

namespace EasyStock.Application.Ports.Output.Persistence.Pagamentos;

/// <summary>Cobrança pendente cujo link já venceu, na varredura cross-tenant do job (S11).</summary>
public sealed record CobrancaPedidoVencida(Guid CobrancaId, Guid EmpresaId, Guid PedidoId, string? ContaRecebedoraId = null);

/// <summary>
/// #1610: link online pendente da conversa perto de vencer, na varredura cross-tenant do lembrete. O lembrete enviado
/// (ou tentado) fica reservado na tabela de idempotência com <see cref="RecursoLembrete"/> e a chave <c>CobrancaId</c>.
/// </summary>
public sealed record CobrancaPedidoVencendo(Guid CobrancaId, Guid EmpresaId, Guid PedidoId)
{
    public const string RecursoLembrete = "JOB cobranca.vencendo";
    public string Chave => CobrancaId.ToString();
}

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
    /// #1610: links online pendentes da conversa que vencem em até <paramref name="antecedencia"/> depois de
    /// <paramref name="agora"/>, de todos os tenants, sem os que já têm lembrete reservado (exceto o que falhou no canal).
    /// </summary>
    Task<IReadOnlyList<CobrancaPedidoVencendo>> ListarPendentesVencendoAsync(
        DateTime agora, TimeSpan antecedencia, int maximo, CancellationToken ct = default);

    /// <summary>
    /// Há alguma cobrança online pendente, em qualquer loja (I3.1)? O webhook que não consegue reconsultar o
    /// pagamento usa isto para decidir entre pedir reenvio e ignorar. Só devolve sim ou não.
    /// </summary>
    Task<bool> ExistePendenteOnlineAsync(CancellationToken ct = default);

    /// <summary>
    /// Devolve à varredura as cobranças que aguardavam credencial e são da conta dada, ou não têm conta
    /// gravada (I3.2). Em todas as lojas; devolve quantas voltaram.
    /// </summary>
    Task<int> RetomarAguardandoCredencialAsync(string contaRecebedoraId, CancellationToken ct = default);

    /// <summary>Há cobrança aguardando credencial, em qualquer loja? Evita consultar o provedor à toa a cada rodada.</summary>
    Task<bool> ExisteAguardandoCredencialAsync(CancellationToken ct = default);
}
