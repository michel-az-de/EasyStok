namespace EasyStock.Application.UseCases.ConfiguracoesLoja;

public sealed record ObterConfiguracaoLojaQuery(Guid EmpresaId, Guid LojaId);

public class ObterConfiguracaoLojaUseCase(
    ILojaRepository lojaRepository,
    IConfiguracaoLojaRepository configuracaoRepository)
{
    public async Task<ConfiguracaoLojaResult> ExecuteAsync(ObterConfiguracaoLojaQuery query)
    {
        var loja = await lojaRepository.GetByIdAsync(query.EmpresaId, query.LojaId)
            ?? throw new InvalidOperationException("Loja nao encontrada.");

        var config = await configuracaoRepository.GetOrDefaultAsync(loja.Id);
        return new ConfiguracaoLojaResult(
            config.LojaId,
            config.DiasAlertaValidade,
            config.DiasAlertaParado,
            config.QuantidadeMinimaPadrao,
            config.QuantidadeCriticaPadrao,
            config.NotificarEstoqueCritico,
            config.NotificarValidade,
            config.NotificarParado,
            config.NotificarReposicao,
            config.FifoAtivo,
            config.Moeda,
            config.Timezone,
            config.KdsHabilitado);
    }
}
