using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

public class RotinaSchedulerTests
{
    private static readonly RotinaScheduler Sut = new();

    private static RotinaNotificacao CriarRotinaCron(string cron) =>
        RotinaNotificacao.Criar("r", "n", TipoEventoNotificacao.ProdutoVencendo,
            TriggerTipoRotina.Cron, "t", CategoriaConteudoNotificacao.Operacional, cron);

    [Fact]
    public void ProximaExecucao_retorna_null_para_rotina_evento()
    {
        var rotina = RotinaNotificacao.Criar("r", "n", TipoEventoNotificacao.ResetSenha,
            TriggerTipoRotina.Evento, "t", CategoriaConteudoNotificacao.Transacional);

        var proxima = Sut.ProximaExecucao(rotina, DateTime.UtcNow);

        proxima.Should().BeNull();
    }

    [Fact]
    public void ProximaExecucao_calcula_proxima_para_cron_diario()
    {
        var rotina = CriarRotinaCron("0 8 * * *");
        var de = new DateTime(2026, 5, 6, 7, 0, 0, DateTimeKind.Utc);

        var proxima = Sut.ProximaExecucao(rotina, de);

        proxima.Should().Be(new DateTime(2026, 5, 6, 8, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void DeveriasExecutar_retorna_false_para_rotina_inativa()
    {
        var rotina = CriarRotinaCron("0 8 * * *");
        // rotina criada como Ativa=false por padrão

        var resultado = Sut.DeveriasExecutar(rotina,
            new DateTime(2026, 5, 6, 7, 55, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 6, 8, 5, 0, DateTimeKind.Utc));

        resultado.Should().BeFalse();
    }

    [Fact]
    public void DeveriasExecutar_retorna_true_quando_horario_passado()
    {
        var rotina = CriarRotinaCron("0 8 * * *");
        rotina.Ativar("admin@x.com");

        var resultado = Sut.DeveriasExecutar(rotina,
            new DateTime(2026, 5, 6, 7, 55, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 6, 8, 5, 0, DateTimeKind.Utc));

        resultado.Should().BeTrue();
    }

    [Fact]
    public void DeveriasExecutar_retorna_false_quando_horario_nao_chegou()
    {
        var rotina = CriarRotinaCron("0 8 * * *");
        rotina.Ativar("admin@x.com");

        var resultado = Sut.DeveriasExecutar(rotina,
            new DateTime(2026, 5, 6, 7, 55, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 6, 7, 58, 0, DateTimeKind.Utc));

        resultado.Should().BeFalse();
    }
}
