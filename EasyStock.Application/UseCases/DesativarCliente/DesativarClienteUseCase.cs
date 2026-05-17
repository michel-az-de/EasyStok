using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.DesativarCliente;

public sealed record DesativarClienteCommand(
    Guid EmpresaId,
    Guid Id,
    Guid? AlteradoPorUserId = null,
    string? AlteradoPorNome = null,
    string? Origem = "web");

public class DesativarClienteUseCase(
    IClienteRepository repo,
    IUnitOfWork uow,
    ILogger<DesativarClienteUseCase> logger)
{
    public async Task<bool> ExecuteAsync(DesativarClienteCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, "Id");

        var cliente = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id);
        if (cliente == null) return false;
        if (!cliente.Ativo) return true;

        cliente.Desativar();

        await repo.AddAlteracaoAsync(new ClienteAlteracao
        {
            Id = Guid.NewGuid(),
            EmpresaId = cliente.EmpresaId,
            ClienteId = cliente.Id,
            AlteradoPorUserId = cmd.AlteradoPorUserId,
            AlteradoPorNome = cmd.AlteradoPorNome,
            Campo = "Ativo",
            ValorAntigo = "true",
            ValorNovo = "false",
            AlteradoEm = DateTime.UtcNow,
            Origem = cmd.Origem
        });

        await repo.UpdateAsync(cliente);
        await uow.CommitAsync();

        logger.LogInformation("Cliente {Id} desativado.", cliente.Id);
        return true;
    }
}
