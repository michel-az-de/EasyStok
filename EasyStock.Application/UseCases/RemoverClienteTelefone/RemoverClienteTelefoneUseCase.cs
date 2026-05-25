using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.RemoverClienteTelefone;

public sealed record RemoverClienteTelefoneCommand(Guid EmpresaId, Guid ClienteId, Guid TelefoneId);

public class RemoverClienteTelefoneUseCase(
    IClienteRepository repo,
    IUnitOfWork uow,
    ILogger<RemoverClienteTelefoneUseCase> logger)
{
    public async Task<bool> ExecuteAsync(RemoverClienteTelefoneCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.ClienteId, "ClienteId");
        UseCaseGuards.EnsureNotEmpty(cmd.TelefoneId, "TelefoneId");

        var removed = await repo.RemoveTelefoneAsync(cmd.EmpresaId, cmd.ClienteId, cmd.TelefoneId);
        if (!removed) return false;

        await uow.CommitAsync();

        logger.LogInformation("Telefone {Id} removido do cliente {ClienteId}.", cmd.TelefoneId, cmd.ClienteId);
        return true;
    }
}
