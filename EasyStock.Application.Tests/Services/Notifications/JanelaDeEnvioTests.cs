using EasyStock.Application.Common;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N5: a janela da rotina adia (nunca suprime) o envio fora de [início, fim), no fuso de Brasília.</summary>
public class JanelaDeEnvioTests
{
    private static readonly TimeOnly Sete = new(7, 0);
    private static readonly TimeOnly VinteEDuas = new(22, 0);

    private static DateTime Brasilia(int dia, int hora, int minuto) =>
        HorarioBrasil.InstanteUtc(new DateOnly(2026, 10, dia), new TimeOnly(hora, minuto));

    [Fact]
    public void ForaDaJanelaAdiaParaAAbertura()
    {
        JanelaDeEnvio.ProximaAbertura(Sete, VinteEDuas, Brasilia(2, 23, 30)).Should().Be(Brasilia(3, 7, 0));

        // Antes de abrir no mesmo dia: abre hoje.
        JanelaDeEnvio.ProximaAbertura(Sete, VinteEDuas, Brasilia(2, 5, 0)).Should().Be(Brasilia(2, 7, 0));
        // O fim é exclusivo.
        JanelaDeEnvio.ProximaAbertura(Sete, VinteEDuas, Brasilia(2, 22, 0)).Should().Be(Brasilia(3, 7, 0));
    }

    [Fact]
    public void DentroDaJanelaNaoAdia()
    {
        JanelaDeEnvio.ProximaAbertura(Sete, VinteEDuas, Brasilia(2, 7, 0)).Should().BeNull("o início é inclusivo");
        JanelaDeEnvio.ProximaAbertura(Sete, VinteEDuas, Brasilia(2, 15, 0)).Should().BeNull();
    }

    [Fact]
    public void JanelaQueViraAMeiaNoite()
    {
        var inicio = new TimeOnly(22, 0);
        var fim = new TimeOnly(6, 0);

        JanelaDeEnvio.ProximaAbertura(inicio, fim, Brasilia(2, 23, 0)).Should().BeNull();
        JanelaDeEnvio.ProximaAbertura(inicio, fim, Brasilia(3, 2, 0)).Should().BeNull();
        JanelaDeEnvio.ProximaAbertura(inicio, fim, Brasilia(2, 12, 0)).Should().Be(Brasilia(2, 22, 0));
        JanelaDeEnvio.ProximaAbertura(inicio, fim, Brasilia(3, 6, 0)).Should().Be(Brasilia(3, 22, 0));
    }

    [Fact]
    public void SemJanelaNaoAdia()
    {
        var agora = Brasilia(2, 23, 30);
        JanelaDeEnvio.ProximaAbertura(null, VinteEDuas, agora).Should().BeNull();
        JanelaDeEnvio.ProximaAbertura(Sete, null, agora).Should().BeNull();
        JanelaDeEnvio.ProximaAbertura(null, null, agora).Should().BeNull();
        JanelaDeEnvio.ProximaAbertura(Sete, Sete, agora).Should().BeNull("pontas iguais não definem janela");
    }

    [Fact]
    public void SegurancaIgnoraAJanela()
    {
        var agora = Brasilia(2, 23, 30);
        JanelaDeEnvio.ProximaAbertura(Sete, VinteEDuas, agora, CategoriaConteudoNotificacao.Seguranca).Should().BeNull();
        JanelaDeEnvio.ProximaAbertura(Sete, VinteEDuas, agora, CategoriaConteudoNotificacao.Operacional)
            .Should().Be(Brasilia(3, 7, 0));
    }

    [Fact]
    public void UsaOFusoDeBrasilia()
    {
        // 23:30 UTC é 20:30 em Brasília: dentro de 07:00 a 22:00, ainda que passe das 22:00 no UTC.
        var utc = new DateTime(2026, 10, 2, 23, 30, 0, DateTimeKind.Utc);
        JanelaDeEnvio.ProximaAbertura(Sete, VinteEDuas, utc).Should().BeNull();

        // 01:00 UTC é 22:00 em Brasília (fim exclusivo): fora, abre 07:00 de Brasília (10:00 UTC).
        var fora = new DateTime(2026, 10, 3, 1, 0, 0, DateTimeKind.Utc);
        JanelaDeEnvio.ProximaAbertura(Sete, VinteEDuas, fora)
            .Should().Be(new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc));
    }
}
