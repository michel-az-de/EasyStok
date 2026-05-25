using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.RemoverClienteDocumento;

public sealed record RemoverClienteDocumentoCommand(Guid EmpresaId, Guid ClienteId, Guid DocumentoId);

public class RemoverClienteDocumentoUseCase(
    IClienteRepository repo,
    IUnitOfWork uow,
    ILogger<RemoverClienteDocumentoUseCase> logger)
{
    public async Task<bool> ExecuteAsync(RemoverClienteDocumentoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.ClienteId, "ClienteId");
        UseCaseGuards.EnsureNotEmpty(cmd.DocumentoId, "DocumentoId");

        var removed = await repo.RemoveDocumentoAsync(cmd.EmpresaId, cmd.ClienteId, cmd.DocumentoId);
        if (!removed) return false;

        await uow.CommitAsync();

        logger.LogInformation("Documento {Id} removido do cliente {ClienteId}.", cmd.DocumentoId, cmd.ClienteId);
        return true;
    }
}
