namespace EasyStock.Application.UseCases.Inventario;

public sealed record CancelarContagemCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid ContagemId);

/// <summary>Cancela uma contagem nao-terminal (-> Cancelada).</summary>
public class CancelarContagemUseCase(
    IContagemRepository repo,
    IUnitOfWork uow,
    ILogger<CancelarContagemUseCase> logger)
{
    public async Task<ContagemResult> ExecuteAsync(CancelarContagemCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.ContagemId, "ContagemId");

        var contagem = await repo.GetByIdComItensAsync(cmd.EmpresaId, cmd.ContagemId)
            ?? throw new UseCaseValidationException("Contagem nao encontrada.");

        contagem.Cancelar(); // valida nao-terminal
        await repo.UpdateAsync(contagem);
        await uow.CommitAsync();

        logger.LogInformation("Contagem {Id} cancelada.", contagem.Id);
        return CriarContagemUseCase.Map(contagem);
    }
}
