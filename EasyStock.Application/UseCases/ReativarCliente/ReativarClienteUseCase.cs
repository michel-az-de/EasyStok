using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.ReativarCliente;

public sealed record ReativarClienteCommand(
    Guid EmpresaId,
    Guid Id,
    Guid? AlteradoPorUserId = null,
    string? AlteradoPorNome = null,
    string? Origem = "web");

public class ReativarClienteUseCase(
    IClienteRepository repo,
    IUnitOfWork uow,
    ILogger<ReativarClienteUseCase> logger)
{
    public async Task<bool> ExecuteAsync(ReativarClienteCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, "Id");

        var cliente = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id);
        if (cliente == null) return false;
        if (cliente.Ativo) return true;

        cliente.Reativar();

        await repo.AddAlteracaoAsync(new ClienteAlteracao
        {
            Id = Guid.NewGuid(),
            ClienteId = cliente.Id,
            AlteradoPorUserId = cmd.AlteradoPorUserId,
            AlteradoPorNome = cmd.AlteradoPorNome,
            Campo = "Ativo",
            ValorAntigo = "false",
            ValorNovo = "true",
            AlteradoEm = DateTime.UtcNow,
            Origem = cmd.Origem
        });

        await repo.UpdateAsync(cliente);
        await uow.CommitAsync();

        logger.LogInformation("Cliente {Id} reativado.", cliente.Id);
        return true;
    }
}
