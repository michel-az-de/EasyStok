using EasyStock.TestHelpers;
using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.Tests.Services.Auth;

public class LimitePedidosAcessoTests
{
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 10, 2, 13, 0, 0, TimeSpan.Zero));
    private readonly LimitePedidosAcesso _limite;
    private readonly FakeCacheComRelogio _cache;

    public LimitePedidosAcessoTests()
    {
        _cache = new FakeCacheComRelogio(_relogio);
        _limite = new LimitePedidosAcesso(_cache);
    }

    [Fact]
    public async Task SextoPedidoDoMesmoIpEm15MinutosEhRecusado()
    {
        for (var i = 0; i < 5; i++)
            (await _limite.TentarAsync("203.0.113.7")).Should().BeTrue($"o pedido {i + 1} cabe nos 5");

        (await _limite.TentarAsync("203.0.113.7")).Should().BeFalse();
        (await _limite.TentarAsync("203.0.113.8")).Should().BeTrue("outro IP tem a própria conta");
    }

    [Fact]
    public async Task JanelaReiniciaDepoisDe15Minutos()
    {
        for (var i = 0; i < 6; i++) await _limite.TentarAsync("203.0.113.7");

        _relogio.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        (await _limite.TentarAsync("203.0.113.7")).Should().BeTrue();
    }

    [Fact]
    public async Task SemIpNaoLimita()
    {
        for (var i = 0; i < 10; i++)
            (await _limite.TentarAsync(null)).Should().BeTrue();
    }

    [Fact]
    public void ChaveGuardaOHashDoIpNuncaOIp()
    {
        LimitePedidosAcesso.Chave("203.0.113.7").Should().NotContain("203.0.113.7").And.StartWith("auth:pedido-acesso:v1:");
    }
}
