using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.AdicionarClienteDocumento;

public sealed record AdicionarClienteDocumentoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid ClienteId,
    [property: Required][property: MaxLength(20)] string Tipo,
    [property: Required][property: MaxLength(60)] string Valor,
    [property: MaxLength(60)] string? Emissor = null,
    DateTime? EmitidoEm = null,
    DateTime? ValidoAte = null,
    bool Principal = false);

public class AdicionarClienteDocumentoUseCase(
    IClienteRepository repo,
    IUnitOfWork uow,
    ILogger<AdicionarClienteDocumentoUseCase> logger)
{
    public async Task<Guid?> ExecuteAsync(AdicionarClienteDocumentoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.ClienteId, "ClienteId");

        var cliente = await repo.GetByIdAsync(cmd.EmpresaId, cmd.ClienteId);
        if (cliente == null) return null;

        // Normalização: se for cpf/cnpj válido, mantém só dígitos
        var valorNormalizado = Cnpj.TryFrom(cmd.Valor)?.Value ?? cmd.Valor.Trim();

        var agora = DateTime.UtcNow;
        var doc = new ClienteDocumento
        {
            Id = Guid.NewGuid(),
            ClienteId = cliente.Id,
            Tipo = string.IsNullOrWhiteSpace(cmd.Tipo) ? "outro" : cmd.Tipo.Trim().ToLowerInvariant(),
            Valor = valorNormalizado,
            Emissor = cmd.Emissor,
            EmitidoEm = cmd.EmitidoEm,
            ValidoAte = cmd.ValidoAte,
            Principal = cmd.Principal,
            CriadoEm = agora,
            AlteradoEm = agora
        };

        await repo.AddDocumentoAsync(doc);
        await uow.CommitAsync();

        logger.LogInformation("Documento {Id} adicionado ao cliente {ClienteId}.", doc.Id, cliente.Id);
        return doc.Id;
    }
}
