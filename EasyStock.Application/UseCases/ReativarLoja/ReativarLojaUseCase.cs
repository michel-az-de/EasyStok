namespace EasyStock.Application.UseCases.ReativarLoja;

public sealed record ReativarLojaCommand(Guid LojaId, Guid EmpresaId);

public class ReativarLojaUseCase(
    ILojaRepository lojaRepository,
    IAssinaturaEmpresaRepository assinaturaRepository,
    IUnitOfWork unitOfWork,
    ILogger<ReativarLojaUseCase> logger)
{
    public async Task ExecuteAsync(ReativarLojaCommand command)
    {
        var loja = await lojaRepository.GetByIdAsync(command.LojaId);
        if (loja is null || loja.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("Loja nao encontrada.");

        // Mesmo limite de CriarLoja (#2562): a contagem só considera lojas ativas, então
        // reativar sem conferir deixava passar do plano.
        if (!loja.Ativa)
        {
            var assinatura = await assinaturaRepository.GetAtivaAsync(command.EmpresaId);
            if (assinatura is not null && !assinatura.Plano!.LojasSaoIlimitadas
                && await lojaRepository.CountByEmpresaAsync(command.EmpresaId) >= assinatura.Plano.LimiteLojas)
                throw new PlanoLimiteAtingidoException("lojas");
        }

        loja.Reativar();

        await lojaRepository.UpdateAsync(loja);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Loja {LojaId} reativada.", loja.Id);
    }
}
