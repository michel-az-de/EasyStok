using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Lotes;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.ConferirEtiqueta;

public sealed record ConferirEtiquetaCommand(
    [property: Required] Guid EmpresaId,
    [property: Required][property: MaxLength(60)] string Codigo,
    [property: Required][property: MaxLength(20)] string Status, // "conferida" | "divergente"
    string? Observacao = null,
    Guid? ConferidaPorUserId = null,
    [property: MaxLength(120)] string? ConferidaPorNome = null);

/// <summary>
/// Conferência de etiqueta individual via scanner — atualiza Status pra
/// "conferida" ou "divergente" com timestamp + quem.
/// </summary>
public class ConferirEtiquetaUseCase(
    ILoteRepository repo,
    IUnitOfWork uow,
    ILogger<ConferirEtiquetaUseCase> logger)
{
    private static readonly HashSet<string> StatusValidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "conferida", "divergente", "consumida", "pendente"
    };

    public async Task<LoteEtiquetaResult?> ExecuteAsync(ConferirEtiquetaCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (string.IsNullOrWhiteSpace(cmd.Codigo))
            throw new UseCaseValidationException("Código da etiqueta é obrigatório.");

        var status = (cmd.Status ?? "").Trim().ToLowerInvariant();
        if (!StatusValidos.Contains(status))
            throw new UseCaseValidationException($"Status inválido: {cmd.Status}");

        var etq = await repo.FindEtiquetaPorCodigoAsync(cmd.EmpresaId, cmd.Codigo.Trim());
        if (etq == null) return null;

        etq.Status = status;
        etq.ConferidaEm = DateTime.UtcNow;
        etq.ConferidaPorUserId = cmd.ConferidaPorUserId;
        etq.ConferidaPorNome = cmd.ConferidaPorNome;
        etq.ObservacaoConferencia = cmd.Observacao;

        await repo.UpdateEtiquetaAsync(etq);
        await uow.CommitAsync();

        logger.LogInformation("Etiqueta {Codigo} -> {Status}.", etq.Codigo, status);
        return new LoteEtiquetaResult(
            etq.Id, etq.LoteId, etq.LoteItemId, etq.Sequencial, etq.Codigo, etq.Status,
            etq.ConferidaEm, etq.ConferidaPorUserId, etq.ConferidaPorNome,
            etq.ObservacaoConferencia, etq.CriadoEm);
    }
}
