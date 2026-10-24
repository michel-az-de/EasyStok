namespace EasyStock.Application.Tests.Common;

/// <summary>Filtro por periodo (desde/ate da query string): Unspecified e sempre horario de Brasilia (revisao da PR #2011).</summary>
public class DataUtcPeriodoTests
{
    [Fact]
    public void Unspecified_com_hora_e_horario_de_Brasilia()
    {
        var ate = new DateTime(2026, 10, 23, 18, 0, 0, DateTimeKind.Unspecified);

        DataUtc.FimDoPeriodoUtc(ate).Should().Be(new DateTime(2026, 10, 23, 21, 0, 0, DateTimeKind.Utc));
        DataUtc.InicioDoPeriodoUtc(ate).Should().Be(new DateTime(2026, 10, 23, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Data_pura_no_fim_inclui_o_dia_inteiro_de_Brasilia()
    {
        var ate = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Unspecified);

        DataUtc.FimDoPeriodoUtc(ate).Should().Be(new DateTime(2026, 10, 24, 3, 0, 0, DateTimeKind.Utc).AddTicks(-10));
    }

    [Fact]
    public void Data_pura_do_ultimo_dia_possivel_nao_estoura()
    {
        var ate = new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Unspecified);

        var act = () => DataUtc.FimDoPeriodoUtc(ate);

        act.Should().NotThrow();
        act()!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Extremos_do_DateTime_nao_estouram(bool maximo)
    {
        var extremo = DateTime.SpecifyKind(maximo ? DateTime.MaxValue : DateTime.MinValue, DateTimeKind.Unspecified);

        var inicio = () => DataUtc.InicioDoPeriodoUtc(extremo);
        var fim = () => DataUtc.FimDoPeriodoUtc(extremo);

        inicio.Should().NotThrow();
        fim.Should().NotThrow();
    }
}
