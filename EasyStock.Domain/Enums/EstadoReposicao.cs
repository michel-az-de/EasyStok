namespace EasyStock.Domain.Enums;

/// <summary>
/// Estado de reposição de um produto (ADR-0039), derivado da quantidade vigente vs limiares
/// resolvidos. Severidade decrescente: Esgotado > Critico > Atencao (ordenação R5).
/// "Precisa repor" (contagem de card/badge/sino) = { Esgotado, Critico }.
/// </summary>
public enum EstadoReposicao
{
    Esgotado,
    Critico,
    Atencao
}
