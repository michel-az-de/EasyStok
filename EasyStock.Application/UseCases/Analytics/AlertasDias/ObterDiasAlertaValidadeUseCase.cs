using EasyStock.Application.Configuration;

namespace EasyStock.Application.UseCases.Analytics.AlertasDias;

public class ObterDiasAlertaValidadeUseCase(
    IConfiguracaoLojaRepository configuracaoLojaRepository,
    IEasyStockConfiguracoes config,
    ILogger<ObterDiasAlertaValidadeUseCase> logger)
{

    public async Task<ObterDiasAlertaValidadeResult> ExecuteAsync(ObterDiasAlertaValidadeCommand cmd)
    {
        if (!cmd.LojaId.HasValue)
        {
            logger.LogInformation("Using default validity alert days: {Dias}", config.DiasAlertaVencimento);
            return new ObterDiasAlertaValidadeResult(config.DiasAlertaVencimento);
        }

        var configuracao = await configuracaoLojaRepository.GetByLojaIdAsync(cmd.LojaId.Value);
        var dias = configuracao?.DiasAlertaValidade ?? config.DiasAlertaVencimento;

        logger.LogInformation("Retrieved validity alert days for loja {LojaId}: {Dias}", cmd.LojaId, dias);

        return new ObterDiasAlertaValidadeResult(dias);
    }
}
