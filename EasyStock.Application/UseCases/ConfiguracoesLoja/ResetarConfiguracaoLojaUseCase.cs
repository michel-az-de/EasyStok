using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.ConfiguracoesLoja;

public sealed record ResetarConfiguracaoLojaCommand(Guid EmpresaId, Guid LojaId);

public class ResetarConfiguracaoLojaUseCase(
    ILojaRepository lojaRepository,
    IConfiguracaoLojaRepository configuracaoRepository,
    IUnitOfWork unitOfWork,
    ILogger<ResetarConfiguracaoLojaUseCase> logger)
{
    public async Task<ConfiguracaoLojaResult> ExecuteAsync(ResetarConfiguracaoLojaCommand command)
    {
        logger.LogInformation("Resetando configuracao da loja {LojaId} para os valores padrao. EmpresaId: {EmpresaId}.", command.LojaId, command.EmpresaId);

        var loja = await lojaRepository.GetByIdAsync(command.EmpresaId, command.LojaId)
            ?? throw new UseCaseValidationException("Loja nao encontrada.");

        var configuracao = await configuracaoRepository.GetByLojaIdAsync(loja.Id);
        var nova = configuracao is null;
        configuracao ??= Domain.Entities.ConfiguracaoLoja.CriarPadrao(loja.Id);
        configuracao.ResetarPadrao();

        if (nova) await configuracaoRepository.AddAsync(configuracao);
        else await configuracaoRepository.UpdateAsync(configuracao);

        await unitOfWork.CommitAsync();

        logger.LogInformation("Configuracao da loja {LojaId} resetada com sucesso.", command.LojaId);

        return new ConfiguracaoLojaResult(
            configuracao.LojaId,
            configuracao.DiasAlertaValidade,
            configuracao.DiasAlertaParado,
            configuracao.QuantidadeMinimaPadrao,
            configuracao.QuantidadeCriticaPadrao,
            configuracao.NotificarEstoqueCritico,
            configuracao.NotificarValidade,
            configuracao.NotificarParado,
            configuracao.NotificarReposicao,
            configuracao.FifoAtivo,
            configuracao.Moeda,
            configuracao.Timezone);
    }
}
