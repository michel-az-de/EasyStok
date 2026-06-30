using EasyStock.Domain.Defaults;
using EasyStock.Domain.Reposicao;
using EasyStock.Domain.Services;

namespace EasyStock.Application.UseCases.Analytics.Reposicao;

/// <summary>
/// Fonte ÚNICA de reposição no nível de aplicação (ADR-0039 / issue 748): carrega a
/// política da loja (DiasCoberturaAlvo / LeadTimePadrao / limiares de config), obtém a
/// projeção por-produto via porta e delega a elegibilidade/sugestão à função pura
/// <see cref="AnalisadorReposicao"/>. Nenhuma superfície recalcula reposição por fora.
/// </summary>
public sealed class ObterReposicaoUseCase(
    IAnalyticsRepository analyticsRepository,
    IConfiguracaoLojaRepository configuracaoRepository,
    ILogger<ObterReposicaoUseCase> logger)
{
    private readonly AnalisadorReposicao analisador = new();

    public async Task<IReadOnlyList<ItemReposicao>> ExecuteAsync(
        ObterReposicaoCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        // Política por loja; sem config (ou sem LojaId) cai nos defaults globais.
        var configuracao = cmd.LojaId.HasValue
            ? await configuracaoRepository.GetByLojaIdAsync(cmd.LojaId.Value).ConfigureAwait(false)
            : null;

        var diasCoberturaAlvo = configuracao?.DiasCoberturaAlvo ?? OperacionalDefaults.DiasCoberturaAlvo;
        var leadTimePadrao = configuracao?.LeadTimePadraoDias ?? OperacionalDefaults.LeadTimePadraoDias;
        int? configMinima = configuracao?.QuantidadeMinimaPadrao;
        int? configCritica = configuracao?.QuantidadeCriticaPadrao;

        var snapshot = await analyticsRepository.GetSnapshotReposicaoAsync(
            cmd.EmpresaId, cmd.LojaId, cmd.DiasHistorico, leadTimePadrao,
            configMinima, configCritica, ct).ConfigureAwait(false);

        var opcoes = new OpcoesReposicao(diasCoberturaAlvo);
        var itens = analisador.Analisar(snapshot, opcoes);

        logger.LogInformation(
            "Reposicao (fonte unica) calculada para empresa {EmpresaId}, loja {LojaId}: "
            + "{Elegiveis} elegiveis de {Total} produtos avaliados.",
            cmd.EmpresaId, cmd.LojaId ?? Guid.Empty, itens.Count, snapshot.Count);

        return itens;
    }
}
