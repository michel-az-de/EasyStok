namespace EasyStock.Application.UseCases.CancelarAssinatura;

public sealed record CancelarAssinaturaCommand(Guid EmpresaId, bool ImediadaOuFimPeriodo = false);

public class CancelarAssinaturaUseCase(
    IAssinaturaEmpresaRepository assinaturaRepo,
    IUnitOfWork uow,
    ILogger<CancelarAssinaturaUseCase> logger)
{
    public async Task ExecuteAsync(CancelarAssinaturaCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var assinatura = await assinaturaRepo.GetAtivaAsync(cmd.EmpresaId)
            ?? throw new UseCaseValidationException("Nenhuma assinatura ativa encontrada.");

        // Por padrão: mantém acesso até fim do período pago (DataFim existente).
        // Se ImediadaOuFimPeriodo=true: cancela imediatamente.
        var dataFim = cmd.ImediadaOuFimPeriodo ? DateTime.UtcNow : assinatura.DataFim;
        assinatura.Cancelar(dataFim);
        await assinaturaRepo.UpdateAsync(assinatura);
        await uow.CommitAsync();

        logger.LogInformation("Assinatura cancelada. EmpresaId={EmpresaId} DataFim={DataFim}",
            cmd.EmpresaId, dataFim);
    }
}
