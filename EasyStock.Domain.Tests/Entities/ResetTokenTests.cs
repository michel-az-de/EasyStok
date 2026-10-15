using EasyStock.Domain.Entities;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

/// <summary>N9: o convite reaproveita <c>reset_tokens</c> com a finalidade <c>Convite</c> e nunca serve como reset.</summary>
public class ResetTokenTests
{
    [Fact]
    public void FinalidadePadraoEhReset()
    {
        var token = ResetToken.Criar(Guid.NewGuid(), "hash", DateTime.UtcNow.AddMinutes(30), null, null);

        token.Finalidade.Should().Be(FinalidadeResetToken.Reset);
    }

    [Fact]
    public void ConviteNaoServeComoReset()
    {
        var convite = ResetToken.Criar(
            Guid.NewGuid(), "hash", DateTime.UtcNow.AddHours(72), null, null, FinalidadeResetToken.Convite, canal: "Email");

        convite.Finalidade.Should().Be("Convite");
        convite.ServePara(FinalidadeResetToken.Reset).Should().BeFalse();
        convite.ServePara(FinalidadeResetToken.Convite).Should().BeTrue();
    }
}
