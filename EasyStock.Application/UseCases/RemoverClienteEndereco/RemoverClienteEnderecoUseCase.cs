using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

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

        var cliente = await repo.GetByIdAsync(cmd.EmpresaId, cmd.ClienteId);
        if (cliente == null) return false;

        await repo.RemoveEnderecoAsync(cmd.EnderecoId);
        await uow.CommitAsync();

        logger.LogInformation("Endereco {Id} removido do cliente {ClienteId}.", cmd.EnderecoId, cmd.ClienteId);
        return true;
    }
}
