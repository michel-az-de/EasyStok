namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// CRM leve do cliente (S24): tags e notas internas. Separado de <see cref="IClienteRepository"/>
/// para não obrigar quem só lê cadastro a conhecer tag e nota.
/// </summary>
public interface IClienteCrmRepository
{
    /// <summary>Cliente rastreado com <see cref="Cliente.Tags"/> carregadas; null quando não é da empresa.</summary>
    Task<Cliente?> ObterComTagsAsync(Guid empresaId, Guid clienteId, CancellationToken ct = default);

    /// <summary>Notas mais recentes primeiro.</summary>
    Task<IReadOnlyList<ClienteNota>> ListarNotasAsync(Guid empresaId, Guid clienteId, int maximo, CancellationToken ct = default);

    Task AdicionarNotaAsync(ClienteNota nota, CancellationToken ct = default);

    /// <summary>true só quando o pedido existe na empresa e é deste cliente.</summary>
    Task<bool> PedidoEhDoClienteAsync(Guid empresaId, Guid pedidoId, Guid clienteId, CancellationToken ct = default);
}
