using EasyStock.Application.Common;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N5: a janela da rotina vira <c>ProximaTentativaEm</c> do outbox; o <c>enviarApos</c> do payload vale se for mais tarde.</summary>
public class NotificadorServiceJanelaTests
{
    /// <summary>Janela de uma hora que começa daqui a duas horas (no relógio de Brasília): o agora fica fora dela.</summary>
    private static (TimeOnly Inicio, TimeOnly Fim) JanelaDaquiADuasHoras()
    {
        var alvo = TimeOnly.FromDateTime(HorarioBrasil.Agora().AddHours(2));
        var inicio = new TimeOnly(alvo.Hour, alvo.Minute);
        return (inicio, inicio.AddHours(1));
    }

    [Fact]
    public async Task EventoForaDaJanelaGravaProximaTentativaEmNaAbertura()
    {
        var f = new NotificadorServiceFixture(CanalNotificacao.Email);
        var rotina = f.UsarRotina(["Email"]);
        var (inicio, fim) = JanelaDaquiADuasHoras();
        rotina.DefinirJanela(inicio, fim);
        f.UsarTemplate(CanalNotificacao.Email);
        var antes = DateTime.UtcNow;

        await f.Service.AvaliarEventoAsync(f.NovoEvento());

        var msg = f.Gravadas.Should().ContainSingle().Subject;
        msg.ProximaTentativaEm.Should().BeAfter(antes.AddHours(1).AddMinutes(55));
        msg.ProximaTentativaEm.Should().BeBefore(antes.AddHours(2).AddMinutes(1));
    }

    [Fact]
    public async Task SegurancaIgnoraAJanelaNoServico()
    {
        var f = new NotificadorServiceFixture(CanalNotificacao.Email);
        var rotina = f.UsarRotina(["Email"], CategoriaConteudoNotificacao.Seguranca);
        var (inicio, fim) = JanelaDaquiADuasHoras();
        rotina.DefinirJanela(inicio, fim);
        f.UsarTemplate(CanalNotificacao.Email);

        await f.Service.AvaliarEventoAsync(f.NovoEvento());

        f.Gravadas.Should().ContainSingle().Which.ProximaTentativaEm.Should().BeBefore(DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task EnviarAposDoPayloadVenceSeForMaisTarde()
    {
        var f = new NotificadorServiceFixture(CanalNotificacao.Email);
        var rotina = f.UsarRotina(["Email"]);
        var (inicio, fim) = JanelaDaquiADuasHoras();
        rotina.DefinirJanela(inicio, fim);
        f.UsarTemplate(CanalNotificacao.Email);
        var depois = DateTime.UtcNow.AddHours(10);
        var payload = $$"""{"email":"maria@example.com","nome":"Maria","enviarApos":"{{depois:O}}"}""";

        await f.Service.AvaliarEventoAsync(f.NovoEvento(payload));

        f.Gravadas.Should().ContainSingle().Which.ProximaTentativaEm.Should().BeCloseTo(depois, TimeSpan.FromSeconds(2));
    }
}
