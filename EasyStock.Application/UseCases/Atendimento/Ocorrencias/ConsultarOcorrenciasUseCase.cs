using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Ocorrencias;

/// <summary>Leitura das ocorrências para o console (S27).</summary>
public sealed class ConsultarOcorrenciasUseCase(IOcorrenciaRepository repo)
{
    public const int LimitePadrao = 100;

    public async Task<IReadOnlyList<OcorrenciaDto>> ListarAsync(Guid empresaId, StatusOcorrencia? status, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        return (await repo.ListarAsync(empresaId, status, LimitePadrao, ct)).Select(OcorrenciaDto.De).ToList();
    }

    public async Task<OcorrenciaDto?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        return await repo.ObterAsync(empresaId, id, ct) is { } o ? OcorrenciaDto.De(o) : null;
    }
}
