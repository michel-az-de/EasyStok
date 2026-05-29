namespace EasyStock.Application.UseCases.RemoverClienteEndereco;

public sealed record RemoverClienteEnderecoCommand(Guid EmpresaId, Guid ClienteId, Guid EnderecoId);

public class RemoverClienteEnderecoUseCase(
    IClienteRepository repo,
    IUnitOfWork uow,
    ILogger<RemoverClienteEnderecoUseCase> logger)
{
    public async Task<bool> ExecuteAsync(RemoverClienteEnderecoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.ClienteId, "ClienteId");
        UseCaseGuards.EnsureNotEmpty(cmd.EnderecoId, "EnderecoId");

        var removed = await repo.RemoveEnderecoAsync(cmd.EmpresaId, cmd.ClienteId, cmd.EnderecoId);
        if (!removed) return false;

        await uow.CommitAsync();

        logger.LogInformation("Endereco {Id} removido do cliente {ClienteId}.", cmd.EnderecoId, cmd.ClienteId);
        return true;
    }
}
