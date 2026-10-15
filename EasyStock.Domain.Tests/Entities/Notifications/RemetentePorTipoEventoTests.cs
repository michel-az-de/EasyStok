using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Notifications;

/// <summary>N6: o remetente é propriedade do tipo do evento, sem valor padrão.</summary>
public class RemetentePorTipoEventoTests
{
    [Fact]
    public void TodoValorDoEnumTemRemetenteDeclarado()
    {
        // Falha no dia em que entrar valor novo no enum sem classificação: não existe "padrão" que esconda o esquecimento.
        foreach (var tipo in Enum.GetValues<TipoEventoNotificacao>())
        {
            var act = () => RemetentePorTipoEvento.De(tipo);
            act.Should().NotThrow($"{tipo} precisa ser classificado como Loja ou Plataforma em RemetentePorTipoEvento");
        }
    }

    [Fact]
    public void ValorForaDoEnumNaoTemRemetente()
    {
        var act = () => RemetentePorTipoEvento.De((TipoEventoNotificacao)9999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(TipoEventoNotificacao.PedidoPagoConfirmado)]
    [InlineData(TipoEventoNotificacao.PedidoEmPreparo)]
    [InlineData(TipoEventoNotificacao.PedidoSaiuParaEntrega)]
    [InlineData(TipoEventoNotificacao.PedidoEntregue)]
    [InlineData(TipoEventoNotificacao.PedidoReagendado)]
    [InlineData(TipoEventoNotificacao.AvaliacaoSolicitada)]
    [InlineData(TipoEventoNotificacao.ReembolsoEfetuado)]
    [InlineData(TipoEventoNotificacao.CampanhaMarketing)]
    [InlineData(TipoEventoNotificacao.CampanhaLembreteEncerramento)]
    public void AvisosAoClienteSaoDaLoja(TipoEventoNotificacao tipo) =>
        RemetentePorTipoEvento.De(tipo).Should().Be(OrigemRemetente.Loja);

    [Theory]
    [InlineData(TipoEventoNotificacao.ResetSenha)]
    [InlineData(TipoEventoNotificacao.ConviteAcesso)]
    [InlineData(TipoEventoNotificacao.IncidenteSistema)]
    [InlineData(TipoEventoNotificacao.PrazoEstourado)]
    [InlineData(TipoEventoNotificacao.ResumoDiario)]
    [InlineData(TipoEventoNotificacao.ContatoAlterado)]
    [InlineData(TipoEventoNotificacao.FaturaVencida)]
    public void TiposDeSistemaSaoDaPlataforma(TipoEventoNotificacao tipo) =>
        RemetentePorTipoEvento.De(tipo).Should().Be(OrigemRemetente.Plataforma);
}
