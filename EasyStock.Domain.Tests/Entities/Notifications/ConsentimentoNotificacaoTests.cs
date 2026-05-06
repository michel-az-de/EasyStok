using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Notifications;

public class ConsentimentoNotificacaoTests
{
    [Fact]
    public void Registrar_optIn_nao_grava_motivo()
    {
        var c = ConsentimentoNotificacao.Registrar(
            Guid.NewGuid(), CanalNotificacao.Email, CategoriaConteudoNotificacao.Marketing,
            optIn: true, atualizadoPor: "user@x.com",
            motivoOptOut: "ignorar");

        c.OptIn.Should().BeTrue();
        c.MotivoOptOut.Should().BeNull();
    }

    [Fact]
    public void Registrar_optOut_preserva_motivo()
    {
        var c = ConsentimentoNotificacao.Registrar(
            Guid.NewGuid(), CanalNotificacao.Email, CategoriaConteudoNotificacao.Marketing,
            optIn: false, atualizadoPor: "user@x.com",
            motivoOptOut: "muitos emails");

        c.OptIn.Should().BeFalse();
        c.MotivoOptOut.Should().Be("muitos emails");
    }
}
