using EasyStock.Application.UseCases.Analytics.Reposicao;
using EasyStock.Domain.Reposicao;

namespace EasyStock.Application.Tests.UseCases.Analytics;

public class ReposicaoMapperTests
{
    [Fact]
    public void ToReposicaoSugerida_preserva_o_shape_do_contrato_legado()
    {
        // Anti-regressao: o Web desserializa ReposicaoSugerida; a fonte por-produto (ADR-0039)
        // deve preencher todos os campos (velocidade/custo do contrato enriquecido) sem trocar semantica.
        var produtoId = Guid.NewGuid();
        var item = new ItemReposicao(
            produtoId, null, "Cafe", 3m, 5, 2, EstadoReposicao.Atencao,
            8m, ConfiancaReposicao.Alta, "Abaixo do minimo (3 de 5)", 4, Guid.NewGuid(),
            VelocidadeMediaDia: 1.5m, CustoEstimadoReposicao: 80m);

        var dto = item.ToReposicaoSugerida();

        dto.ProdutoId.Should().Be(produtoId);
        dto.NomeProduto.Should().Be("Cafe");
        dto.QuantidadeAtual.Should().Be(3);
        dto.QuantidadeMinima.Should().Be(5);
        dto.QuantidadeSugeridaReposicao.Should().Be(8);
        dto.VelocidadeSaidaDiaria.Should().Be(1.5m);
        dto.DiasAteRuptura.Should().Be(4);
        dto.CustoEstimadoReposicao.Should().Be(80m);
        // Identidade de lote nao existe por-produto: vazio/nulo (views usam NomeProduto).
        dto.ItemEstoqueId.Should().Be(Guid.Empty);
        dto.CodigoInterno.Should().BeNull();
    }
}
