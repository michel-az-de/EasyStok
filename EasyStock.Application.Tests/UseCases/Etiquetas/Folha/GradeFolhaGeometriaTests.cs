using EasyStock.Application.UseCases.Etiquetas.Folha;

namespace EasyStock.Application.Tests.UseCases.Etiquetas.Folha;

public class GradeFolhaGeometriaTests
{
    [Fact]
    public void PimacoA4256_SegueOGabaritoDoFabricante()
    {
        var grade = GradeFolha.PimacoA4256();

        grade.Colunas.Should().Be(3);
        grade.Linhas.Should().Be(7);
        grade.PorPagina.Should().Be(21);
        grade.Celulas[0].Should().Be(new CelulaGrade(7.21m, 15.15m, 63.5m, 38.1m));
        grade.Celulas[1].XMm.Should().Be(73.25m, "passo horizontal de 66,04 mm");
        grade.Celulas[3].YMm.Should().Be(53.25m, "segunda linha, passo vertical de 38,1 mm");
        grade.Celulas[^1].Should().Be(new CelulaGrade(139.29m, 243.75m, 63.5m, 38.1m));
    }

    [Fact]
    public void QuantasCabem_52x75_Da3x3CentralizadaNaA4()
    {
        var grade = GradeFolha.QuantasCabem(52m, 75m, 5m, 3m);

        grade.Should().NotBeNull();
        grade!.Colunas.Should().Be(3);
        grade.Linhas.Should().Be(3);
        grade.Celulas[0].Should().Be(new CelulaGrade(24m, 33m, 52m, 75m));
        grade.Celulas[^1].Should().Be(new CelulaGrade(134m, 189m, 52m, 75m));
    }

    [Fact]
    public void QuantasCabem_150x100_Da1x2()
    {
        var grade = GradeFolha.QuantasCabem(150m, 100m, 5m, 3m);

        grade!.Colunas.Should().Be(1);
        grade.Linhas.Should().Be(2);
        grade.Celulas.Select(c => (c.XMm, c.YMm)).Should().Equal((30m, 47m), (30m, 150m));
    }

    [Fact]
    public void QuantasCabem_MaiorQueAA4_DevolveNulo()
    {
        GradeFolha.QuantasCabem(250m, 100m, 5m, 3m).Should().BeNull();
    }
}
