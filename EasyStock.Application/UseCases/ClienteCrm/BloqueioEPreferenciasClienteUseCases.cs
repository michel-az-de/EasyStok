namespace EasyStock.Application.UseCases.ClienteCrm;

/// <summary>
/// Bloqueio do cliente (S24). Vale em todos os canais: a primeira mensagem de uma conversa nova vai
/// direto para a dona, sem saudação, e <c>criar_pedido</c> recusa com <c>cliente_bloqueado</c>.
/// </summary>
public sealed class DefinirBloqueioClienteUseCase(IClienteRepository clientes, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<BloqueioClienteResult> BloquearAsync(Guid empresaId, Guid clienteId, string? motivo, CancellationToken ct = default)
    {
        var cliente = await ObterAsync(empresaId, clienteId);
        cliente.Bloquear(motivo, relogio.GetUtcNow().UtcDateTime);
        await unitOfWork.CommitAsync();
        return BloqueioClienteResult.De(cliente);
    }

    public async Task<BloqueioClienteResult> DesbloquearAsync(Guid empresaId, Guid clienteId, CancellationToken ct = default)
    {
        var cliente = await ObterAsync(empresaId, clienteId);
        cliente.Desbloquear(relogio.GetUtcNow().UtcDateTime);
        await unitOfWork.CommitAsync();
        return BloqueioClienteResult.De(cliente);
    }

    private async Task<EasyStock.Domain.Entities.Cliente> ObterAsync(Guid empresaId, Guid clienteId) =>
        await clientes.GetByIdAsync(empresaId, clienteId) ?? throw new ClienteCrmNaoEncontradoException(clienteId);
}

/// <summary>Campo nulo = não mexe.</summary>
public sealed record DefinirPreferenciasClienteCommand(
    Guid EmpresaId, Guid ClienteId, bool? AvisosStatusAtivos, bool? ConsentiuMarketing);

/// <summary>
/// Preferências do cliente (S24): avisos de andamento do pedido e o opt-in de marketing do cadastro.
/// O consentimento por canal que decide o envio é o da S38 (<c>ConsentimentoContato</c>).
/// </summary>
public sealed class DefinirPreferenciasClienteUseCase(IClienteRepository clientes, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<PreferenciasClienteResult> ExecuteAsync(DefinirPreferenciasClienteCommand command, CancellationToken ct = default)
    {
        var cliente = await clientes.GetByIdAsync(command.EmpresaId, command.ClienteId)
            ?? throw new ClienteCrmNaoEncontradoException(command.ClienteId);

        var agora = relogio.GetUtcNow().UtcDateTime;
        if (command.AvisosStatusAtivos is { } avisos) cliente.DefinirAvisosStatus(avisos, agora);
        if (command.ConsentiuMarketing is { } consentiu) cliente.DefinirConsentimentoMarketing(consentiu, agora);

        await unitOfWork.CommitAsync();
        return PreferenciasClienteResult.De(cliente);
    }
}
