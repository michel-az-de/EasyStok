using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.ReativarLoja;

public sealed record ReativarLojaCommand(Guid LojaId, Guid EmpresaId);

public class ReativarLojaUseCase(
    ILojaRepository lojaRepository,
    IUnitOfWork unitOfWork,
    ILogger<ReativarLojaUseCase> logger)
{
    public async Task ExecuteAsync(ReativarLojaCommand command)
    {
        var loja = await lojaRepository.GetByIdAsync(command.LojaId);
        if (loja is null || loja.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("Loja nao encontrada.");

        loja.Ativa = true;
        loja.AlteradoEm = DateTime.UtcNow;

        await lojaRepository.UpdateAsync(loja);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Loja {LojaId} reativada.", loja.Id);
    }
}
