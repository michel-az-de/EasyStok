namespace EasyStock.Application.UseCases.AlterarPlano;

public sealed record AlterarPlanoCommand(Guid EmpresaId, Guid NovoPlanoId);

public sealed record AlterarPlanoResult(Guid AssinaturaId, Guid NovoPlanoId, string NovoPlanNome, decimal NovoPreco);

public class AlterarPlanoUseCase(
    IAssinaturaEmpresaRepository assinaturaRepo,
    IPlanoRepository planoRepo,
    IUnitOfWork uow,
    ILogger<AlterarPlanoUseCase> logger)
{
    public async Task<AlterarPlanoResult> ExecuteAsync(AlterarPlanoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.NovoPlanoId, "NovoPlanoId");

        var assinatura = await assinaturaRepo.GetAtivaAsync(cmd.EmpresaId)
            ?? throw new UseCaseValidationException("Nenhuma assinatura ativa encontrada.");

        var novoPlano = await planoRepo.GetByIdAsync(cmd.NovoPlanoId)
            ?? throw new UseCaseValidationException("Plano não encontrado.");

        if (!novoPlano.Ativo)
            throw new UseCaseValidationException("O plano selecionado não está ativo.");

        if (assinatura.PlanoId == cmd.NovoPlanoId)
            throw new UseCaseValidationException("A assinatura já está nesse plano.");

        var planoAnteriorId = assinatura.PlanoId;
        assinatura.PlanoId = cmd.NovoPlanoId;
        assinatura.AlteradoEm = DateTime.UtcNow;

        await assinaturaRepo.UpdateAsync(assinatura);
        await uow.CommitAsync();

        logger.LogInformation(
            "Plano alterado. EmpresaId={EmpresaId} De={PlanoAnterior} Para={PlanoNovo}",
            cmd.EmpresaId, planoAnteriorId, cmd.NovoPlanoId);

        return new AlterarPlanoResult(assinatura.Id, novoPlano.Id, novoPlano.Nome, novoPlano.PrecoMensal);
    }
}
