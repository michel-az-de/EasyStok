namespace EasyStock.Domain.Reposicao;

/// <summary>
/// Contrato ÚNICO de saída do cálculo de reposição (ADR-0039). Toda superfície
/// (Dashboard, Análises, badges, "Gerar do estoque baixo", sino, InteligenciaLojas)
/// consome este record; nenhuma recalcula elegibilidade por fora. Quebra de contrato
/// (mudar campo) deve quebrar o teste de snapshot do contrato.
/// </summary>
public sealed record ItemReposicao(
    Guid ProdutoId,
    Guid? VariacaoId,
    string Nome,
    decimal QuantidadeVigente,
    int NivelMinimo,
    int NivelCritico,
    EstadoReposicao Estado,
    decimal QuantidadeSugerida,
    ConfiancaReposicao Confianca,
    string Motivo,
    int? DiasAteRuptura,
    Guid? FornecedorId,
    decimal VelocidadeMediaDia,
    decimal CustoEstimadoReposicao);
