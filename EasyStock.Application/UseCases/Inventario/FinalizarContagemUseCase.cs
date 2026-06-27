namespace EasyStock.Application.UseCases.Inventario;

public sealed record FinalizarContagemCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid ContagemId);

/// <summary>EmAndamento -> Finalizada. MVP exige todos os lotes conferidos (apply parcial = V2).</summary>
public class FinalizarContagemUseCase(
    IContagemRepository repo,
    IUnitOfWork uow,
    ILogger<FinalizarContagemUseCase> logger)
{
    public async Task<ContagemResult> ExecuteAsync(FinalizarContagemCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.ContagemId, "ContagemId");

        var contagem = await repo.GetByIdComItensAsync(cmd.EmpresaId, cmd.ContagemId)
            ?? throw new UseCaseValidationException("Contagem nao encontrada.");

        var naoConferidos = contagem.Itens.Count(i => !i.Conferido);
        if (naoConferidos > 0)
            throw new UseCaseValidationException(
                $"Nao e possivel finalizar: {naoConferidos} lote(s) ainda nao conferido(s).");

        contagem.Finalizar(DateTime.UtcNow); // valida EmAndamento
        await repo.UpdateAsync(contagem);
        await uow.CommitAsync();

        logger.LogInformation("Contagem {Id} finalizada ({N} lotes).", contagem.Id, contagem.Itens.Count);
        return CriarContagemUseCase.Map(contagem);
    }
}
