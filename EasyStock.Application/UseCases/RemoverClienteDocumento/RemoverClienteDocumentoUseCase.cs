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

        var cliente = await repo.GetByIdAsync(cmd.EmpresaId, cmd.ClienteId);
        if (cliente == null) return false;

        await repo.RemoveDocumentoAsync(cmd.DocumentoId);
        await uow.CommitAsync();

        logger.LogInformation("Documento {Id} removido do cliente {ClienteId}.", cmd.DocumentoId, cmd.ClienteId);
        return true;
    }
}
