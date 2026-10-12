using EasyStock.Application.Ports.Output.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed class ListarModelosAtendimentoUseCase(ICatalogoModelosAtendimento catalogo)
{
    public Task<IReadOnlyList<ModeloAtendimento>> ExecuteAsync(Guid empresaId, CancellationToken ct = default) =>
        empresaId == Guid.Empty
            ? throw new UseCaseValidationException("Empresa é obrigatória.")
            : catalogo.ListarAsync(empresaId, ct);
}
