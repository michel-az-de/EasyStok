using EasyStock.TestHelpers;
using EasyStock.Application.Services.Auth;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.Services.Auth;

public class LimpezaTokensAcessoTests
{
    [Fact]
    public async Task ApagaEmLoteSoOQueExpirouHaMaisDe24h()
    {
        var agora = new DateTimeOffset(2026, 10, 2, 13, 0, 0, TimeSpan.Zero);
        var relogio = new FakeTimeProvider(agora);
        var tokens = new FakeResetTokenRepository();
        var usuario = Guid.NewGuid();
        ResetToken Token(string h, TimeSpan expiraEmRelacaoAAgora) =>
            ResetToken.Criar(usuario, h, agora.UtcDateTime + expiraEmRelacaoAAgora, null, null);
        var velho = Token("velho", TimeSpan.FromHours(-25));
        var usadoAntigo = Token("usado-antigo", TimeSpan.FromHours(-30));
        usadoAntigo.Usado = true;
        var recemExpirado = Token("recem-expirado", TimeSpan.FromHours(-23));
        var vigente = Token("vigente", TimeSpan.FromMinutes(20));
        foreach (var t in new[] { velho, usadoAntigo, recemExpirado, vigente }) await tokens.AddAsync(t);
        var limpeza = new LimpezaTokensAcesso(tokens, relogio, Substitute.For<ILogger<LimpezaTokensAcesso>>());

        var apagados = await limpeza.ExecutarAsync();

        apagados.Should().Be(2);
        tokens.CorteDaLimpeza.Should().Be(agora.UtcDateTime.AddHours(-24));
        tokens.Linhas.Select(l => l.TokenHash).Should().BeEquivalentTo("recem-expirado", "vigente");
    }
}
