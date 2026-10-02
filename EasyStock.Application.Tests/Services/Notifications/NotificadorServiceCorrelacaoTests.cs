using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

public class NotificadorServiceCorrelacaoTests
{
    [Fact]
    public async Task EnfileirarEventoRepassaCorrelationId()
    {
        var f = new NotificadorServiceFixture();
        EventoNotificacao? gravado = null;
        f.Evento.When(e => e.AddAsync(Arg.Any<EventoNotificacao>(), Arg.Any<CancellationToken>()))
            .Do(c => gravado = c.Arg<EventoNotificacao>());

        var id = await f.Service.EnfileirarEventoAsync(
            TipoEventoNotificacao.FaturaVencida, f.EmpresaId, "{}", correlationId: "reset:abc");

        gravado.Should().NotBeNull();
        gravado!.CorrelationId.Should().Be("reset:abc");
        gravado.Id.Should().Be(id);
    }

    [Fact]
    public async Task SemCorrelationIdGeraUmNovo()
    {
        var f = new NotificadorServiceFixture();
        EventoNotificacao? gravado = null;
        f.Evento.When(e => e.AddAsync(Arg.Any<EventoNotificacao>(), Arg.Any<CancellationToken>()))
            .Do(c => gravado = c.Arg<EventoNotificacao>());

        await f.Service.EnfileirarEventoAsync(TipoEventoNotificacao.FaturaVencida, f.EmpresaId, "{}");

        gravado!.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }
}
