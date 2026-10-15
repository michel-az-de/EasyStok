using EasyStock.Application.Ports.Output.Persistence.Operacao;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

public sealed record ListarImpressoesPendentesInput(Guid EmpresaId, int Limite = ListarImpressoesPendentesUseCase.LimitePadrao);

/// <summary>Polling do consumidor da fila (S20): as pendentes da empresa, mais antigas primeiro.</summary>
public sealed class ListarImpressoesPendentesUseCase(IImpressaoPendenteRepository repo)
{
    public const int LimitePadrao = 10;
    public const int LimiteMaximo = 50;

    public async Task<IReadOnlyList<ImpressaoPendenteDto>> ExecuteAsync(ListarImpressoesPendentesInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);

        var pendentes = await repo.ListarPendentesAsync(input.EmpresaId, Math.Clamp(input.Limite, 1, LimiteMaximo), ct);
        return pendentes.Select(ImpressaoPendenteDto.De).ToList();
    }
}
