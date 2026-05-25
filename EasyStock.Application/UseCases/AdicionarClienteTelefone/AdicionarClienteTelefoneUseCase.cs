using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.AdicionarClienteTelefone;

public sealed record AdicionarClienteTelefoneCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid ClienteId,
    [property: Required][property: MaxLength(32)] string Numero,
    [property: MaxLength(20)] string? Tipo = null,
    bool Whatsapp = false,
    bool Principal = false,
    [property: MaxLength(255)] string? Observacao = null);

public class AdicionarClienteTelefoneUseCase(
    IClienteRepository repo,
    IUnitOfWork uow,
    ILogger<AdicionarClienteTelefoneUseCase> logger)
{
    public async Task<Guid?> ExecuteAsync(AdicionarClienteTelefoneCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.ClienteId, "ClienteId");

        var cliente = await repo.GetByIdAsync(cmd.EmpresaId, cmd.ClienteId);
        if (cliente == null) return null;

        var numeroNormalizado = Telefone.TryFrom(cmd.Numero)?.Value ?? cmd.Numero.Trim();

        var agora = DateTime.UtcNow;
        var tel = new ClienteTelefone
        {
            Id = Guid.NewGuid(),
            ClienteId = cliente.Id,
            Tipo = cmd.Tipo,
            Numero = numeroNormalizado,
            Whatsapp = cmd.Whatsapp,
            Principal = cmd.Principal,
            Observacao = cmd.Observacao,
            CriadoEm = agora,
            AlteradoEm = agora
        };

        await repo.AddTelefoneAsync(tel);
        await uow.CommitAsync();

        logger.LogInformation("Telefone {Id} adicionado ao cliente {ClienteId}.", tel.Id, cliente.Id);
        return tel.Id;
    }
}
