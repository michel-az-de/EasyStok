using EasyStock.Domain.Reposicao;

namespace EasyStock.Application.UseCases.Analytics.Reposicao;

/// <summary>
/// Mapeia o contrato único <see cref="ItemReposicao"/> (por-produto, ADR-0039) para o DTO
/// legado <see cref="ReposicaoSugerida"/> consumido pelo Web, preservando o shape de campos
/// para não quebrar a desserialização. ItemEstoqueId/CodigoInterno são identidade de LOTE que
/// a fonte por-produto não possui; as views usam NomeProduto (sempre presente), então ficam
/// vazio/nulo sem regressão de exibição.
/// </summary>
public static class ReposicaoMapper
{
    public static ReposicaoSugerida ToReposicaoSugerida(this ItemReposicao item) =>
        new(
            ItemEstoqueId: Guid.Empty,
            ProdutoId: item.ProdutoId,
            NomeProduto: item.Nome,
            CodigoInterno: null,
            QuantidadeAtual: (int)item.QuantidadeVigente,
            QuantidadeMinima: item.NivelMinimo,
            QuantidadeSugeridaReposicao: (int)Math.Ceiling(item.QuantidadeSugerida),
            VelocidadeSaidaDiaria: item.VelocidadeMediaDia,
            DiasAteRuptura: item.DiasAteRuptura,
            CustoEstimadoReposicao: item.CustoEstimadoReposicao);
}
