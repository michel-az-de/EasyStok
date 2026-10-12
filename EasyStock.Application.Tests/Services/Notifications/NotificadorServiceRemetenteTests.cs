using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N6: o remetente da mensagem sai do tipo do evento e é gravado no outbox ao enfileirar.</summary>
public class NotificadorServiceRemetenteTests
{
    [Theory]
    [InlineData(TipoEventoNotificacao.IncidenteSistema, OrigemRemetente.Plataforma)]
    [InlineData(TipoEventoNotificacao.FaturaVencida, OrigemRemetente.Plataforma)]
    [InlineData(TipoEventoNotificacao.PedidoEntregue, OrigemRemetente.Loja)]
    [InlineData(TipoEventoNotificacao.PedidoReagendado, OrigemRemetente.Loja)]
    public async Task OutboxNasceComORemetenteDoTipo(TipoEventoNotificacao tipo, OrigemRemetente esperado)
    {
        var f = new NotificadorServiceFixture(CanalNotificacao.Email);
        f.UsarRotina(["Email"], tipo: tipo);
        f.UsarTemplate(CanalNotificacao.Email, tipo: tipo);

        await f.Service.AvaliarEventoAsync(f.NovoEvento(tipo: tipo));

        f.Gravadas.Should().ContainSingle().Which.Remetente.Should().Be(esperado);
    }
}
