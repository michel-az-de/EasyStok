using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.DesativarLoja;

public sealed record DesativarLojaCommand(Guid LojaId, Guid EmpresaId);

public class DesativarLojaUseCase(
    ILojaRepository lojaRepository,
    IUnitOfWork unitOfWork,
    ILogger<DesativarLojaUseCase> logger)
{
    public async Task ExecuteAsync(DesativarLojaCommand command)
    {
        var loja = await lojaRepository.GetByIdAsync(command.LojaId);
        if (loja is null || loja.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("Loja nao encontrada.");

        loja.Desativar();

        await lojaRepository.UpdateAsync(loja);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Loja {LojaId} desativada.", loja.Id);
    }
}
