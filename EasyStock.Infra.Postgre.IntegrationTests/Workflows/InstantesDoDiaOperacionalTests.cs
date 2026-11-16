using EasyStock.Application.Common;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

public sealed class InstantesDoDiaOperacionalTests
{
    private static readonly TimeSpan Recuo = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Avanco = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData("2026-11-16T03:02:20Z")] // 00:02 em São Paulo: o caso que quebrou o CI
    [InlineData("2026-11-16T02:59:58Z")] // 23:59:58 do dia anterior: a borda de antes da meia-noite
    [InlineData("2026-11-16T03:00:00Z")] // a própria meia-noite
    [InlineData("2026-11-16T15:30:00Z")] // meio do dia: nada muda
    public void Instantes_derivados_caem_no_mesmo_dia_operacional_e_mantem_a_ordem(string agoraIso)
    {
        var agora = DateTime.Parse(agoraIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

        var ancora = InstantesDoDiaOperacional.Ancora(agora, Recuo, Avanco);
        var instantes = new[]
        {
            ancora - Recuo, ancora.AddMinutes(-3), ancora.AddMinutes(-2), ancora.AddMinutes(-1), ancora, ancora + Avanco,
        };

        instantes.Select(HorarioBrasil.DataOperacional).Distinct().Should().ContainSingle(
            "todos os lançamentos semeados pertencem ao dia que a prova consulta");
        instantes.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        HorarioBrasil.DataOperacional(ancora).Should().Be(HorarioBrasil.DataOperacional(agora),
            "o dia consultado é o dia operacional de agora, o mesmo que o relógio real do código de produção usa");
    }

    [Fact]
    public void Em_horario_comum_a_ancora_e_o_proprio_agora()
    {
        var agora = new DateTime(2026, 11, 16, 15, 30, 0, DateTimeKind.Utc);

        InstantesDoDiaOperacional.Ancora(agora, Recuo, Avanco).Should().Be(agora);
    }

    [Fact]
    public void A_derivacao_antiga_cruzava_a_meia_noite()
    {
        var agora = new DateTime(2026, 11, 16, 3, 2, 20, DateTimeKind.Utc);

        HorarioBrasil.DataOperacional(agora.AddMinutes(-5)).Should().NotBe(HorarioBrasil.DataOperacional(agora),
            "é por isso que o dia não pode ser derivado de agora menos 5 minutos");
    }
}
