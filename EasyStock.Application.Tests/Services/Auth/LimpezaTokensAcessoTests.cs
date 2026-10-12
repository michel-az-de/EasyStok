using EasyStock.TestHelpers;
using EasyStock.Application.Ports.Output.Persistence;
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
        var limpeza = new LimpezaTokensAcesso(tokens, Substitute.For<IRefreshTokenRepository>(),
            Substitute.For<IEmailConfirmationTokenRepository>(), relogio, Substitute.For<ILogger<LimpezaTokensAcesso>>());

        var apagados = await limpeza.ExecutarAsync();

        apagados.Should().Be(2);
        tokens.CorteDaLimpeza.Should().Be(agora.UtcDateTime.AddHours(-24));
        tokens.Linhas.Select(l => l.TokenHash).Should().BeEquivalentTo("recem-expirado", "vigente");
    }

    private static readonly DateTimeOffset Agora = new(2026, 10, 2, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Corte = Agora.UtcDateTime.AddHours(-24);
    private readonly IResetTokenRepository _reset = Substitute.For<IResetTokenRepository>();
    private readonly IRefreshTokenRepository _refresh = Substitute.For<IRefreshTokenRepository>();
    private readonly IEmailConfirmationTokenRepository _email = Substitute.For<IEmailConfirmationTokenRepository>();

    private LimpezaTokensAcesso Limpeza() =>
        new(_reset, _refresh, _email, new FakeTimeProvider(Agora), Substitute.For<ILogger<LimpezaTokensAcesso>>());

    [Fact]
    public async Task ApagaTambemRefreshTokensEConfirmacoesDeEmailComAMesmaCarencia()
    {
        // #1566: DeleteExpiredAsync das duas tabelas nunca teve chamador; elas só cresciam.
        _reset.ApagarExpiradosAsync(Corte, Arg.Any<CancellationToken>()).Returns(1);
        _refresh.DeleteExpiredAsync(Corte, Arg.Any<CancellationToken>()).Returns(3);
        _email.DeleteExpiredAsync(Corte, Arg.Any<CancellationToken>()).Returns(4);

        var apagados = await Limpeza().ExecutarAsync();

        apagados.Should().Be(8);
        await _refresh.Received(1).DeleteExpiredAsync(Corte, Arg.Any<CancellationToken>());
        await _email.Received(1).DeleteExpiredAsync(Corte, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FalhaNumaTabelaNaoImpedeALimpezaDasOutras()
    {
        _reset.ApagarExpiradosAsync(Corte, Arg.Any<CancellationToken>()).Returns<int>(_ => throw new InvalidOperationException("timeout"));
        _refresh.DeleteExpiredAsync(Corte, Arg.Any<CancellationToken>()).Returns<int>(_ => throw new InvalidOperationException("timeout"));
        _email.DeleteExpiredAsync(Corte, Arg.Any<CancellationToken>()).Returns(4);

        var apagados = await Limpeza().ExecutarAsync();

        apagados.Should().Be(4);
        await _refresh.Received(1).DeleteExpiredAsync(Corte, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelamentoDoHostInterrompeARodada()
    {
        using var cts = new CancellationTokenSource();
        _reset.ApagarExpiradosAsync(Corte, Arg.Any<CancellationToken>()).Returns<int>(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        var act = () => Limpeza().ExecutarAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _refresh.DidNotReceiveWithAnyArgs().DeleteExpiredAsync(default, default);
    }
}
