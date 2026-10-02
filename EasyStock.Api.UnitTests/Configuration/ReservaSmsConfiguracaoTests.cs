using EasyStock.Api.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Api.UnitTests.Configuration;

/// <summary>S60 (#1391): a reserva por SMS só liga com a chave e um provedor real (o stub finge o envio).</summary>
public class ReservaSmsConfiguracaoTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("true", null, false)]
    [InlineData("true", "stub", false)]
    [InlineData("true", "Stub", false)]
    [InlineData("false", "twilio", false)]
    [InlineData("true", "twilio", true)]
    [InlineData("true", "zenvia", true)]
    public void LigaSoComChaveEProvedorReal(string? ligada, string? provedor, bool ativa)
    {
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ReservaSmsConfiguracao.ChaveLigada] = ligada,
            [ReservaSmsConfiguracao.ChaveProvedor] = provedor,
        }).Build();

        ReservaSmsConfiguracao.Ler(configuracao).Ativa.Should().Be(ativa);
    }
}
