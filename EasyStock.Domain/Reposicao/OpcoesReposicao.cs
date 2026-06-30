namespace EasyStock.Domain.Reposicao;

/// <summary>
/// Parâmetros de política de reposição (ADR-0039). Configuráveis, sem hardcode:
/// <paramref name="DiasCoberturaAlvo"/> vem de ConfiguracaoLoja; o piso de histórico para
/// considerar a velocidade confiável também é parametrizável (R3).
/// </summary>
/// <param name="DiasCoberturaAlvo">Dias de cobertura desejados além do lead time (R3).</param>
/// <param name="DiasHistoricoMinimoConfianca">Abaixo deste nº de dias de histórico, a confiança é Baixa e usa-se fallback repor-até-mínimo.</param>
public sealed record OpcoesReposicao(
    int DiasCoberturaAlvo,
    int DiasHistoricoMinimoConfianca = 14);
