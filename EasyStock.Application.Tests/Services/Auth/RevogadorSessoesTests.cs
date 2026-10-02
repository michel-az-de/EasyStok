using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.Services.Auth;

/// <summary>
/// #1352 (N7): o <see cref="RevogadorSessoes"/> derruba as sessões de um usuário na hora: carimbo
/// (todo JWT emitido antes dele deixa de valer), refresh tokens ativos num UPDATE e a chave do cache
/// do validador.
/// </summary>
public class RevogadorSessoesTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 2, 13, 45, 10, 789, TimeSpan.Zero);
    private static readonly DateTime CorteEsperado = new(2026, 10, 2, 13, 45, 10, DateTimeKind.Utc);

    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly Usuario _usuario = Usuario.Criar("Ana", "ana@casadababa.com", "hash");

    private RevogadorSessoes CriarServico() =>
        new(_usuarios, _refreshTokens, _cache, new FakeTimeProvider(Agora), Substitute.For<ILogger<RevogadorSessoes>>());

    [Fact]
    public async Task GravaCarimboRevogaRefreshEApagaOCache()
    {
        _refreshTokens.RevogarSessoesAtivasAsync(_usuario.Id, Agora.UtcDateTime).Returns(3);

        var revogados = await CriarServico().RevogarAsync(_usuario);

        revogados.Should().Be(3);
        _usuario.SessoesValidasDesde.Should().Be(CorteEsperado, "o corte trunca ao segundo");
        // O cache é apagado por último: depois do carimbo gravado, um miss já lê o valor novo.
        Received.InOrder(() =>
        {
            _usuarios.AtualizarSessoesValidasDesdeAsync(_usuario.Id, CorteEsperado);
            _refreshTokens.RevogarSessoesAtivasAsync(_usuario.Id, Agora.UtcDateTime);
            _cache.RemoveAsync(CacheKeys.Sessao(_usuario.Id));
        });
    }

    [Fact]
    public async Task CacheForaDoArNaoImpedeARevogacao()
    {
        _cache.RemoveAsync(Arg.Any<string>()).Returns(Task.FromException(new InvalidOperationException("redis fora")));

        var revogados = await CriarServico().RevogarAsync(_usuario);

        revogados.Should().Be(0);
        await _usuarios.Received(1).AtualizarSessoesValidasDesdeAsync(_usuario.Id, CorteEsperado);
        await _refreshTokens.Received(1).RevogarSessoesAtivasAsync(_usuario.Id, Agora.UtcDateTime);
    }

    [Fact]
    public void ChaveDoCacheTemOFormatoDoValidador()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        CacheKeys.Sessao(id).Should().Be("sessao:v1:0f8fad5bd9cb469fa16570867728950e");
    }
}
