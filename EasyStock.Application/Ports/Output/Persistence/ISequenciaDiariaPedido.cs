namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Contador do número do dia do pedido (S53), um por empresa e dia de produção. <see cref="ProximoAsync"/> é
/// atômico no banco: dois pagamentos simultâneos nunca recebem o mesmo número e nenhum falha por isso. Roda na
/// transação aberta, então um rollback devolve o número (sem buraco quando a confirmação desfaz).
/// </summary>
public interface ISequenciaDiariaPedido
{
    /// <summary>Próximo número (1, 2, 3...) da empresa no <paramref name="dia"/>.</summary>
    Task<int> ProximoAsync(Guid empresaId, DateOnly dia, CancellationToken ct = default);
}
