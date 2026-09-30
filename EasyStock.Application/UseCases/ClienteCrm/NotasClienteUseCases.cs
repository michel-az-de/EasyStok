namespace EasyStock.Application.UseCases.ClienteCrm;

public sealed class ListarNotasClienteUseCase(IClienteRepository clientes, IClienteCrmRepository crm)
{
    public const int Maximo = 100;

    public async Task<IReadOnlyList<ClienteNotaResult>> ExecuteAsync(Guid empresaId, Guid clienteId, CancellationToken ct = default)
    {
        _ = await clientes.GetByIdAsync(empresaId, clienteId) ?? throw new ClienteCrmNaoEncontradoException(clienteId);
        var notas = await crm.ListarNotasAsync(empresaId, clienteId, Maximo, ct);
        return notas.Select(ClienteNotaResult.De).ToList();
    }
}

public sealed record AdicionarNotaClienteCommand(
    Guid EmpresaId, Guid ClienteId, string Texto, string Autor, Guid? PedidoId = null, Guid? MensagemId = null);

/// <summary>Nota interna datada; com <c>PedidoId</c>, o pedido tem de ser deste cliente (senão 400).</summary>
public sealed class AdicionarNotaClienteUseCase(
    IClienteRepository clientes, IClienteCrmRepository crm, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<ClienteNotaResult> ExecuteAsync(AdicionarNotaClienteCommand command, CancellationToken ct = default)
    {
        _ = await clientes.GetByIdAsync(command.EmpresaId, command.ClienteId)
            ?? throw new ClienteCrmNaoEncontradoException(command.ClienteId);

        if (command.PedidoId is { } pedidoId
            && !await crm.PedidoEhDoClienteAsync(command.EmpresaId, pedidoId, command.ClienteId, ct))
            throw new NotaPedidoDeOutroClienteException(pedidoId);

        var nota = ClienteNota.Criar(command.EmpresaId, command.ClienteId, command.Texto, command.Autor,
            relogio.GetUtcNow().UtcDateTime, command.PedidoId, command.MensagemId);

        await crm.AdicionarNotaAsync(nota, ct);
        await unitOfWork.CommitAsync();
        return ClienteNotaResult.De(nota);
    }
}
