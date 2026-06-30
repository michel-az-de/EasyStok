namespace EasyStock.Domain.Enums;

/// <summary>
/// Confiança da sugestão de reposição (ADR-0039, R3). Baixa quando o histórico de saída é
/// curto demais para estimar velocidade: usa-se fallback "repor até o mínimo".
/// </summary>
public enum ConfiancaReposicao
{
    Alta,
    Baixa
}
