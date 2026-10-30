namespace EasyStock.Application.UseCases.ClienteCrm;

public sealed record PaginaNotasClienteResult(IReadOnlyList<ClienteNotaResult> Itens, bool TemMais, int? ProximaPagina);
public sealed record NotaClienteDaMesaResult(Guid ClienteId, string ClienteNome, ClienteNotaResult Nota);

public sealed class ListarNotasClienteUseCase(IClienteRepository clientes, IClienteCrmRepository crm)
{
    public const int Maximo = 100;

    public async Task<IReadOnlyList<ClienteNotaResult>> ExecuteAsync(Guid empresaId, Guid clienteId, CancellationToken ct = default)
    {
        _ = await clientes.GetByIdAsync(empresaId, clienteId) ?? throw new ClienteCrmNaoEncontradoException(clienteId);
        var notas = await crm.ListarNotasAsync(empresaId, clienteId, Maximo, ct);
        return notas.Select(ClienteNotaResult.De).ToList();
    }

    public async Task<PaginaNotasClienteResult> PaginaAsync(Guid empresaId, Guid clienteId, int pagina, int tamanho, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        if (pagina is < 1 or int.MaxValue || tamanho is < 1 or > 50 || ((long)pagina - 1) * tamanho > int.MaxValue)
            throw new UseCaseValidationException("Informe uma página válida e entre 1 e 50 notas por página.");
        await ClienteAsync(empresaId, clienteId);
        var notas = await crm.ListarPaginaNotasAsync(empresaId, clienteId, (pagina - 1) * tamanho, tamanho + 1, ct);
        if (notas.Any(n => n.EmpresaId != empresaId || n.ClienteId != clienteId))
            throw new ClienteCrmNaoEncontradoException(clienteId);
        var temMais = notas.Count > tamanho;
        return new(notas.Take(tamanho).Select(ClienteNotaResult.De).ToList(), temMais, temMais ? pagina + 1 : null);
    }

    public async Task<NotaClienteDaMesaResult> ObterAsync(Guid empresaId, Guid clienteId, Guid notaId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var cliente = await ClienteAsync(empresaId, clienteId);
        var nota = await crm.ObterNotaAsync(empresaId, clienteId, notaId, ct);
        if (nota is null || nota.EmpresaId != empresaId || nota.ClienteId != clienteId || nota.Id != notaId)
            throw new ClienteCrmNaoEncontradoException(clienteId);
        return new(cliente.Id, cliente.Nome, ClienteNotaResult.De(nota));
    }

    private async Task<EasyStock.Domain.Entities.Cliente> ClienteAsync(Guid empresaId, Guid clienteId)
    {
        var cliente = await clientes.GetByIdAsync(empresaId, clienteId);
        if (cliente is null || cliente.EmpresaId != empresaId)
            throw new ClienteCrmNaoEncontradoException(clienteId);
        return cliente;
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
