using EasyStock.Application.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Analytics.AlertasDias;

public class ObterDiasAlertaParadoUseCase(
    IConfiguracaoLojaRepository configuracaoLojaRepository,
    IEasyStockConfiguracoes config,
    ILogger<ObterDiasAlertaParadoUseCase> logger)
{

    public async Task<ObterDiasAlertaParadoResult> ExecuteAsync(ObterDiasAlertaParadoCommand cmd)
    {
        if (!cmd.LojaId.HasValue)
        {
            logger.LogInformation("Using default idle item alert days: {Dias}", config.DiasItemParado);
            return new ObterDiasAlertaParadoResult(config.DiasItemParado);
        }

        var configuracao = await configuracaoLojaRepository.GetByLojaIdAsync(cmd.LojaId.Value);
        var dias = configuracao?.DiasAlertaParado ?? config.DiasItemParado;

        logger.LogInformation("Retrieved idle item alert days for loja {LojaId}: {Dias}", cmd.LojaId, dias);

        return new ObterDiasAlertaParadoResult(dias);
    }
}
