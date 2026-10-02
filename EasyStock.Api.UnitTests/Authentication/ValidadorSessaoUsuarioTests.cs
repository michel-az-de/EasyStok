using System.Security.Claims;
using EasyStock.Api.Authentication;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Infra.Async;
using EasyStock.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Api.UnitTests.Authentication;

/// <summary>
/// #1352 (N7): o validador do JWT confere, a cada requisição, se a sessão ainda vale: o usuário existe e está
/// ativo, e o token foi emitido (<c>iat</c>, em segundos inteiros) a partir do corte <c>SessoesValidasDesde</c>.
/// Cache de 60 s por usuário por cima do banco.
/// </summary>
public class ValidadorSessaoUsuarioTests
{
    // Corte gravado pelo RevogadorSessoes: sempre em segundo inteiro.
    private static readonly DateTimeOffset Corte = new(2026, 10, 2, 13, 45, 10, TimeSpan.Zero);

    private readonly Guid _usuarioId = Guid.NewGuid();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly FakeTimeProvider _relogio = new(Corte);
    private readonly CacheComRelogio _cache;
    private readonly Dictionary<string, string?> _configuracao = [];

    public ValidadorSessaoUsuarioTests() => _cache = new CacheComRelogio(_relogio);

    private ValidadorSessaoUsuario Criar(ICacheService? cache = null) => new(
        cache ?? _cache,
        _usuarios,
        new ConfigurationBuilder().AddInMemoryCollection(_configuracao).Build(),
        NullLogger<ValidadorSessaoUsuario>.Instance);

    private void UsuarioNoBanco(bool ativo = true, DateTimeOffset? corte = null) =>
        _usuarios.ObterSessaoAsync(_usuarioId).Returns(new SessaoDoUsuario(ativo, corte?.UtcDateTime));

    private ClaimsPrincipal Token(DateTimeOffset emitidoEm) =>
        Principal(_usuarioId.ToString(), emitidoEm.ToUnixTimeSeconds().ToString());

    private static ClaimsPrincipal Principal(string? sub, string? iat)
    {
        var claims = new List<Claim>();
        if (sub is not null) claims.Add(new Claim("sub", sub));
        if (iat is not null) claims.Add(new Claim("iat", iat));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    [Fact]
    public async Task TokenAnteriorAoCorteFalha()
    {
        UsuarioNoBanco(corte: Corte);

        (await Criar().ValidarAsync(Token(Corte.AddSeconds(-1)))).Should().BeFalse();
    }

    [Fact]
    public async Task TokenNoMesmoSegundoDoCorteVale()
    {
        // Janela de 1 s aceita e documentada: o iat é em segundos inteiros, então o token emitido no mesmo
        // segundo do reset passa. O emitido depois também.
        UsuarioNoBanco(corte: Corte);
        var validador = Criar();

        (await validador.ValidarAsync(Token(Corte))).Should().BeTrue();
        (await validador.ValidarAsync(Token(Corte.AddSeconds(1)))).Should().BeTrue();
        (await validador.ValidarAsync(Token(Corte.AddHours(2)))).Should().BeTrue();
    }

    [Fact]
    public async Task UsuarioSemCorteComTokenQualquerVale()
    {
        UsuarioNoBanco(corte: null);

        (await Criar().ValidarAsync(Token(Corte.AddYears(-1)))).Should().BeTrue("nulo quer dizer que nunca revogou");
    }

    [Fact]
    public async Task UsuarioInativoOuInexistenteFalha()
    {
        UsuarioNoBanco(ativo: false);
        (await Criar().ValidarAsync(Token(Corte.AddHours(1)))).Should().BeFalse("conta inativa");

        _usuarios.ObterSessaoAsync(_usuarioId).Returns((SessaoDoUsuario?)null);
        await _cache.RemoveAsync(CacheKeys.Sessao(_usuarioId));
        (await Criar().ValidarAsync(Token(Corte.AddHours(1)))).Should().BeFalse("usuário que não existe");
    }

    [Theory]
    [InlineData(null, "1790000000")]
    [InlineData("nao-e-guid", "1790000000")]
    [InlineData("00000000-0000-0000-0000-000000000000", "1790000000")]
    [InlineData("usuario", null)]
    [InlineData("usuario", "")]
    [InlineData("usuario", "ontem")]
    public async Task TokenSemIatOuSemSubFalha(string? sub, string? iat)
    {
        UsuarioNoBanco();
        var sujeito = sub == "usuario" ? _usuarioId.ToString() : sub;

        (await Criar().ValidarAsync(Principal(sujeito, iat))).Should().BeFalse();
        await _usuarios.DidNotReceiveWithAnyArgs().ObterSessaoAsync(default);
    }

    [Fact]
    public async Task HitDeCacheNaoLeOBanco()
    {
        UsuarioNoBanco(corte: Corte);
        var validador = Criar();

        await validador.ValidarAsync(Token(Corte));
        await validador.ValidarAsync(Token(Corte.AddSeconds(5)));
        await validador.ValidarAsync(Token(Corte.AddSeconds(-5)));

        await _usuarios.Received(1).ObterSessaoAsync(_usuarioId);
    }

    [Fact]
    public async Task ApagarAChaveFazOProximoPedidoLerOBanco()
    {
        UsuarioNoBanco(corte: null);
        var validador = Criar();
        var token = Token(Corte);
        (await validador.ValidarAsync(token)).Should().BeTrue();

        // Alguém revoga: o corte vai para depois do token e o RevogadorSessoes apaga a chave.
        UsuarioNoBanco(corte: Corte.AddMinutes(1));
        (await validador.ValidarAsync(token)).Should().BeTrue("o cache ainda guarda o valor antigo");
        await _cache.RemoveAsync(CacheKeys.Sessao(_usuarioId));

        (await validador.ValidarAsync(token)).Should().BeFalse("sem a chave, o pedido lê o corte novo no banco");
        await _usuarios.Received(2).ObterSessaoAsync(_usuarioId);
    }

    [Fact]
    public async Task CacheExpiraEmSessentaSegundos()
    {
        UsuarioNoBanco(corte: Corte);
        var validador = Criar();
        await validador.ValidarAsync(Token(Corte));
        await _usuarios.Received(1).ObterSessaoAsync(_usuarioId);

        _relogio.Advance(TimeSpan.FromSeconds(59));
        await validador.ValidarAsync(Token(Corte));
        await _usuarios.Received(1).ObterSessaoAsync(_usuarioId);

        _relogio.Advance(TimeSpan.FromSeconds(2)); // 61 s desde a leitura
        await validador.ValidarAsync(Token(Corte));
        await _usuarios.Received(2).ObterSessaoAsync(_usuarioId);
    }

    [Fact]
    public async Task ChaveDesligadaNaoValida()
    {
        _configuracao["Auth:SessoesRevogaveis"] = "false";
        UsuarioNoBanco(ativo: false, corte: Corte.AddDays(1));
        var validador = Criar();

        (await validador.ValidarAsync(Token(Corte.AddDays(-30)))).Should().BeTrue("a checagem some");
        (await validador.ValidarAsync(Principal(null, null))).Should().BeTrue("nem os claims são conferidos");
        await _usuarios.DidNotReceiveWithAnyArgs().ObterSessaoAsync(default);
        _cache.Itens.Should().BeEmpty();
    }

    [Theory]
    [InlineData("true")]
    [InlineData(null)]
    [InlineData("talvez")]
    public async Task ChaveLigadaOuInvalidaMantemAChecagem(string? valor)
    {
        // Padrão true; valor que não é booleano não desliga a segurança por engano.
        _configuracao["Auth:SessoesRevogaveis"] = valor;
        UsuarioNoBanco(corte: Corte);

        (await Criar().ValidarAsync(Token(Corte.AddSeconds(-1)))).Should().BeFalse();
    }

    [Fact]
    public async Task UsuarioInexistenteTambemFicaEmCache()
    {
        _usuarios.ObterSessaoAsync(_usuarioId).Returns((SessaoDoUsuario?)null);
        var validador = Criar();

        await validador.ValidarAsync(Token(Corte));
        await validador.ValidarAsync(Token(Corte));

        await _usuarios.Received(1).ObterSessaoAsync(_usuarioId);
    }

    [Fact]
    public async Task CacheForaDoArCaiNoBancoEValida()
    {
        UsuarioNoBanco(corte: Corte);
        var cacheQuebrado = Substitute.For<ICacheService>();
        cacheQuebrado.GetAsync<SessaoDoUsuario>(Arg.Any<string>()).ThrowsAsync(new InvalidOperationException("redis fora"));
        cacheQuebrado.SetAsync(Arg.Any<string>(), Arg.Any<SessaoDoUsuario>(), Arg.Any<TimeSpan?>()).ThrowsAsync(new InvalidOperationException("redis fora"));
        var validador = Criar(cacheQuebrado);

        (await validador.ValidarAsync(Token(Corte))).Should().BeTrue();
        (await validador.ValidarAsync(Token(Corte.AddSeconds(-1)))).Should().BeFalse("a regra vale mesmo sem cache");
    }

    [Fact]
    public async Task ValorGuardadoSobreviveAoJsonDoCacheDeProducao()
    {
        // Em produção ICacheService é o RedisCacheService sobre IDistributedCache (memória por processo, ou Redis):
        // o valor vai e volta por JSON. Se a desserialização falhasse, todo pedido leria o banco.
        UsuarioNoBanco(corte: Corte);
        var distribuido = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var validador = Criar(new RedisCacheService(distribuido, NullLogger<RedisCacheService>.Instance));

        (await validador.ValidarAsync(Token(Corte))).Should().BeTrue();
        (await validador.ValidarAsync(Token(Corte.AddSeconds(-1)))).Should().BeFalse();
        await _usuarios.Received(1).ObterSessaoAsync(_usuarioId);
    }

    /// <summary>Cache em memória que respeita o TTL pelo relógio injetado, para provar os 60 s sem esperar.</summary>
    private sealed class CacheComRelogio(TimeProvider relogio) : ICacheService
    {
        public Dictionary<string, (object Valor, DateTimeOffset? ExpiraEm)> Itens { get; } = [];

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
        {
            Itens[key] = (value!, ttl is { } validade ? relogio.GetUtcNow() + validade : null);
            return Task.CompletedTask;
        }

        public Task<T?> GetAsync<T>(string key) =>
            Task.FromResult(Itens.TryGetValue(key, out var item) && (item.ExpiraEm is null || relogio.GetUtcNow() < item.ExpiraEm)
                ? (T?)item.Valor
                : default);

        public Task RemoveAsync(string key)
        {
            Itens.Remove(key);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(IEnumerable<string> keys) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string key) => throw new NotSupportedException();
        public Task<long> IncrementAsync(string key, long value = 1) => throw new NotSupportedException();
        public Task SetExpiryAsync(string key, TimeSpan ttl) => throw new NotSupportedException();
    }
}
