namespace EasyStock.Application.UseCases.ClienteCrm;

public sealed class ListarTagsClienteUseCase(IClienteCrmRepository crm)
{
    public async Task<TagsClienteResult> ExecuteAsync(Guid empresaId, Guid clienteId, CancellationToken ct = default)
    {
        var cliente = await crm.ObterComTagsAsync(empresaId, clienteId, ct)
            ?? throw new ClienteCrmNaoEncontradoException(clienteId);

        var tags = cliente.Tags.OrderBy(t => t.Tag, StringComparer.Ordinal).Select(ClienteTagResult.De).ToList();
        return new TagsClienteResult(tags, ClienteTag.Sugeridas);
    }
}

public sealed record AdicionarTagClienteCommand(Guid EmpresaId, Guid ClienteId, string Tag);

/// <summary>Tag marcada pela dona no console (<see cref="OrigemClienteTag.Dona"/>). Repetida → 409.</summary>
public sealed class AdicionarTagClienteUseCase(IClienteCrmRepository crm, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<ClienteTagResult> ExecuteAsync(AdicionarTagClienteCommand command, CancellationToken ct = default)
    {
        var cliente = await crm.ObterComTagsAsync(command.EmpresaId, command.ClienteId, ct)
            ?? throw new ClienteCrmNaoEncontradoException(command.ClienteId);

        var tag = cliente.AdicionarTag(command.Tag, OrigemClienteTag.Dona, relogio.GetUtcNow().UtcDateTime)
            ?? throw new ClienteTagDuplicadaException(ClienteTag.Normalizar(command.Tag));

        await unitOfWork.CommitAsync();
        return ClienteTagResult.De(tag);
    }
}

public sealed record RemoverTagClienteCommand(Guid EmpresaId, Guid ClienteId, string Tag);

public sealed class RemoverTagClienteUseCase(IClienteCrmRepository crm, IUnitOfWork unitOfWork)
{
    /// <returns><c>false</c> quando o cliente não tinha a tag.</returns>
    public async Task<bool> ExecuteAsync(RemoverTagClienteCommand command, CancellationToken ct = default)
    {
        var cliente = await crm.ObterComTagsAsync(command.EmpresaId, command.ClienteId, ct)
            ?? throw new ClienteCrmNaoEncontradoException(command.ClienteId);

        if (!cliente.RemoverTag(command.Tag)) return false;

        await unitOfWork.CommitAsync();
        return true;
    }
}
