namespace EasyStock.Application.UseCases.ConfiguracoesLoja;

public sealed record ConfiguracaoLojaResult(
    Guid LojaId,
    int DiasAlertaValidade,
    int DiasAlertaParado,
    int QuantidadeMinimaPadrao,
    int QuantidadeCriticaPadrao,
    bool NotificarEstoqueCritico,
    bool NotificarValidade,
    bool NotificarParado,
    bool NotificarReposicao,
    bool FifoAtivo,
    string Moeda,
    string Timezone,
    bool KdsHabilitado = false);
