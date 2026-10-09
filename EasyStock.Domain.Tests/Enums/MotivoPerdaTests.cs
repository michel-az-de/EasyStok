using EasyStock.Domain.Enums;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Enums;

/// <summary>M2.6 (#1511): o motivo de perda é a natureza da saída, ida e volta.</summary>
public class MotivoPerdaTests
{
    [Theory]
    [InlineData(MotivoPerda.Vencido, NaturezaMovimentacaoEstoque.Vencimento)]
    [InlineData(MotivoPerda.PerdaNoPreparo, NaturezaMovimentacaoEstoque.Perda)]
    [InlineData(MotivoPerda.Doacao, NaturezaMovimentacaoEstoque.Doacao)]
    [InlineData(MotivoPerda.Degustacao, NaturezaMovimentacaoEstoque.UsoInterno)]
    [InlineData(MotivoPerda.Outro, NaturezaMovimentacaoEstoque.Prejuizo)]
    public void MotivoVaiENaturezaVolta(MotivoPerda motivo, NaturezaMovimentacaoEstoque natureza)
    {
        motivo.Natureza().Should().Be(natureza);
        natureza.MotivoDaPerda().Should().Be(motivo);
    }

    [Theory]
    [InlineData(NaturezaMovimentacaoEstoque.Ajuste)]
    [InlineData(NaturezaMovimentacaoEstoque.Venda)]
    [InlineData(NaturezaMovimentacaoEstoque.Estorno)]
    public void AjusteVendaEEstorno_NaoSaoPerda(NaturezaMovimentacaoEstoque natureza) =>
        natureza.MotivoDaPerda().Should().BeNull("o card antigo misturava ajuste de contagem com perda");
}
