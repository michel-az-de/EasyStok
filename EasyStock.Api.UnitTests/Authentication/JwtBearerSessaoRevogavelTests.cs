using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Logout;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Async;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Authentication;

/// <summary>
/// #1352 (N7), o Aceite pelo caminho real: a autenticação de produção (<c>AddEasyStockAuth</c>) num host
/// mínimo, com tokens assinados de verdade. Token emitido antes do corte de sessão do usuário recebe 401 em
/// qualquer rota autenticada (como <c>GET api/auth/me</c>); o emitido depois vale; a chave
/// <c>Auth:SessoesRevogaveis=false</c> desliga a checagem.
/// </summary>
public sealed class JwtBearerSessaoRevogavelTests
{
    private const string ChaveJwt = "chave-de-teste-do-jwt-com-mais-de-32-caracteres";
    private const string Rota = "/api/auth/me";

    private readonly Guid _usuarioId = Guid.NewGuid();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly DateTime _corte = TruncarAoSegundo(DateTime.UtcNow.AddMinutes(-5));

    private static DateTime TruncarAoSegundo(DateTime dataHora) =>
        new(dataHora.Ticks - dataHora.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    [Fact]
    public async Task TokenEmitidoAntesDoCorteRecebe401()
    {
        _usuarios.ObterSessaoAsync(_usuarioId).Returns(new SessaoDoUsuario(true, _corte));

        await using var app = await SubirAsync();
        var resposta = await Chamar(app, Token(_corte.AddMinutes(-10)));

        resposta.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenEmitidoDepoisDoCorteVale()
    {
        _usuarios.ObterSessaoAsync(_usuarioId).Returns(new SessaoDoUsuario(true, _corte));

        await using var app = await SubirAsync();

        (await Chamar(app, Token(_corte.AddMinutes(1)))).Should().Be(HttpStatusCode.OK);
        (await Chamar(app, Token(_corte.AddMilliseconds(500)))).Should().Be(HttpStatusCode.OK, "mesmo segundo do corte vale");
    }

    [Fact]
    public async Task UsuarioQueNuncaRevogouComTokenValidoPassa()
    {
        _usuarios.ObterSessaoAsync(_usuarioId).Returns(new SessaoDoUsuario(true, null));

        await using var app = await SubirAsync();

        (await Chamar(app, Token(DateTime.UtcNow.AddHours(-2)))).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UsuarioInativoRecebe401()
    {
        _usuarios.ObterSessaoAsync(_usuarioId).Returns(new SessaoDoUsuario(false, null));

        await using var app = await SubirAsync();

        (await Chamar(app, Token(DateTime.UtcNow))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChaveDesligadaDeixaOTokenAntigoPassar()
    {
        _usuarios.ObterSessaoAsync(_usuarioId).Returns(new SessaoDoUsuario(true, _corte));

        await using var app = await SubirAsync(new() { ["Auth:SessoesRevogaveis"] = "false" });

        (await Chamar(app, Token(_corte.AddMinutes(-10)))).Should().Be(HttpStatusCode.OK);
        await _usuarios.DidNotReceiveWithAnyArgs().ObterSessaoAsync(default);
    }

    [Fact]
    public async Task SemTokenContinua401()
    {
        await using var app = await SubirAsync();

        (await Chamar(app, token: null)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevogarNaHoraValeNoPedidoSeguinte()
    {
        // O validador guarda o valor por 60 s; o RevogadorSessoes apaga a chave e o pedido seguinte já vê o corte.
        _usuarios.ObterSessaoAsync(_usuarioId).Returns(new SessaoDoUsuario(true, null));
        var cache = new RedisCacheService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<RedisCacheService>.Instance);
        await using var app = await SubirAsync(cache: cache);
        var token = Token(DateTime.UtcNow.AddMinutes(-1));
        (await Chamar(app, token)).Should().Be(HttpStatusCode.OK);

        _usuarios.ObterSessaoAsync(_usuarioId).Returns(new SessaoDoUsuario(true, TruncarAoSegundo(DateTime.UtcNow)));
        await cache.RemoveAsync(EasyStock.Application.UseCases.Common.CacheKeys.Sessao(_usuarioId));

        (await Chamar(app, token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LogoutDerrubaOJwtJaEmitidoNoPedidoSeguinte()
    {
        // #1879, o Aceite pelo caminho real: o logout (caso de uso de produção) grava o corte e o mesmo JWT, que
        // valia 8 h, passa a receber 401. O validador estava com a sessão em cache; o logout a apaga.
        var usuario = Usuario.Criar("Ana", "ana@casadababa.com", "hash");
        usuario.Id = _usuarioId;
        var sessao = new SessaoDoUsuario(true, null);
        _usuarios.ObterSessaoAsync(_usuarioId).Returns(_ => sessao);
        _usuarios.GetByIdAsync(_usuarioId).Returns(usuario);
        _usuarios.AtualizarSessoesValidasDesdeAsync(_usuarioId, Arg.Any<DateTime>())
            .Returns(chamada => { sessao = new SessaoDoUsuario(true, chamada.Arg<DateTime>()); return Task.FromResult(1); });
        var refreshTokens = Substitute.For<IRefreshTokenRepository>();
        var refresh = RefreshToken.Criar(_usuarioId, TokenHashHelper.ComputeSha256Hash("refresh-da-sessao"), DateTime.UtcNow.AddDays(7), null, null);
        refreshTokens.GetByTokenHashAsync(refresh.TokenHash).Returns(refresh);
        var cache = new RedisCacheService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<RedisCacheService>.Instance);
        var logout = new LogoutUseCase(
            refreshTokens, _usuarios,
            new RevogadorSessoes(_usuarios, refreshTokens, cache, TimeProvider.System, NullLogger<RevogadorSessoes>.Instance),
            Substitute.For<IAuditLogRepository>(), Substitute.For<IUnitOfWork>(), NullLogger<LogoutUseCase>.Instance);
        await using var app = await SubirAsync(cache: cache);
        var token = Token(DateTime.UtcNow.AddMinutes(-1));

        (await Chamar(app, token)).Should().Be(HttpStatusCode.OK);
        (await logout.ExecuteAsync(new LogoutCommand("refresh-da-sessao"))).Success.Should().BeTrue();

        (await Chamar(app, token)).Should().Be(HttpStatusCode.Unauthorized);
        (await Chamar(app, Token(DateTime.UtcNow.AddSeconds(2)))).Should().Be(HttpStatusCode.OK, "novo login depois do logout vale");
    }

    [Fact]
    public async Task AutenticacaoDeProducaoRegistraOValidadorNoJwtBearer()
    {
        // Guarda do cabeamento: sem o OnTokenValidated, nenhum teste acima passaria a existir em produção.
        await using var app = await SubirAsync();
        var opcoes = app.Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>()
            .Get(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme);

        opcoes.Events.OnTokenValidated.Should().NotBeNull();
        opcoes.MapInboundClaims.Should().BeFalse("o validador lê sub e iat com o nome do JWT");
    }

    [Fact]
    public async Task PareamentoDeSessaoMantem403ParaOperadorEnquantoAtendimentoViaJwtContinuaAutenticado()
    {
        _usuarios.ObterSessaoAsync(_usuarioId).Returns(new SessaoDoUsuario(true, null));
        var sessao = new SessaoNativa { UsuarioId = _usuarioId, EmpresaId = Guid.NewGuid(), DispositivoId = "android-2184", CriadaEm = DateTime.UtcNow };
        _refreshTokens.ObterSessaoNativaAsync(sessao.Id).Returns(sessao);
        var metodo = typeof(EasyStock.Api.Mobile.Controllers.DevicePairingController).GetMethod("PairSession")!;
        var autorizacao = metodo.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single();
        autorizacao.Policy.Should().Be("Gerente");
        await using var app = await SubirAsync();
        var token = Token(DateTime.UtcNow, sessao, "Operador");
        (await Chamar(app, token)).Should().Be(HttpStatusCode.OK);
        (await Chamar(app, token, "/pair-policy")).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FamiliaNativaRevogadaBloqueiaJwtImediatamente_SemDerrubarWeb()
    {
        _usuarios.ObterSessaoAsync(_usuarioId).Returns(new SessaoDoUsuario(true, null));
        var sessao = new SessaoNativa { UsuarioId = _usuarioId, EmpresaId = Guid.NewGuid(), DispositivoId = "android-2184", CriadaEm = DateTime.UtcNow.AddMinutes(-1) };
        _refreshTokens.ObterSessaoNativaAsync(sessao.Id).Returns(sessao);
        await using var app = await SubirAsync(new() { ["Auth:SessoesRevogaveis"] = "false" });
        var nativo = Token(DateTime.UtcNow, sessao);
        (await Chamar(app, nativo)).Should().Be(HttpStatusCode.OK);
        sessao.RevogadaEm = DateTime.UtcNow;
        (await Chamar(app, nativo)).Should().Be(HttpStatusCode.Unauthorized, "a flag legada não desliga revogação nativa");
        (await Chamar(app, Token(DateTime.UtcNow))).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SessaoNativaNovaUsaUsuarioAtualMesmoComCacheWebInativo()
    {
        _usuarios.ObterSessaoAsync(_usuarioId).Returns(new SessaoDoUsuario(true, null));
        var cache = Substitute.For<ICacheService>();
        cache.GetAsync<SessaoDoUsuario>(Arg.Any<string>()).Returns(new SessaoDoUsuario(false, null));
        var sessao = new SessaoNativa { UsuarioId = _usuarioId, EmpresaId = Guid.NewGuid(), DispositivoId = "android-2184", CriadaEm = DateTime.UtcNow };
        _refreshTokens.ObterSessaoNativaAsync(sessao.Id).Returns(sessao);
        await using var app = await SubirAsync(cache: cache);
        (await Chamar(app, Token(DateTime.UtcNow, sessao))).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CorteDoUsuarioInvalidaFamiliaNativaMesmoSeCacheWebAindaEstaAntigo()
    {
        _usuarios.ObterSessaoAsync(_usuarioId).Returns(new SessaoDoUsuario(true, null));
        var sessao = new SessaoNativa { UsuarioId = _usuarioId, EmpresaId = Guid.NewGuid(), DispositivoId = "android-2184", CriadaEm = DateTime.UtcNow.AddMinutes(-1) };
        _refreshTokens.ObterSessaoNativaAsync(sessao.Id).Returns(sessao);
        await using var app = await SubirAsync();
        var token = Token(DateTime.UtcNow, sessao);
        (await Chamar(app, token)).Should().Be(HttpStatusCode.OK);
        // Outra réplica corta o usuário antes do INSERT da nova família e o cache local ainda não foi invalidado.
        _usuarios.ObterSessaoAsync(_usuarioId).Returns(new SessaoDoUsuario(true, DateTime.UtcNow));
        (await Chamar(app, token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FamiliaNativaNaoTrocaEmpresaNemAparelhoENaoRessuscitaComIatNovo()
    {
        _usuarios.ObterSessaoAsync(_usuarioId).Returns(new SessaoDoUsuario(true, _corte));
        var sessao = new SessaoNativa { UsuarioId = _usuarioId, EmpresaId = Guid.NewGuid(), DispositivoId = "android-2184", CriadaEm = _corte.AddMinutes(-1) };
        _refreshTokens.ObterSessaoNativaAsync(sessao.Id).Returns(sessao);
        await using var app = await SubirAsync();
        (await Chamar(app, Token(DateTime.UtcNow, sessao))).Should().Be(HttpStatusCode.Unauthorized);
        sessao.CriadaEm = _corte.AddMinutes(1);
        var token = Token(DateTime.UtcNow, sessao);
        (await Chamar(app, token)).Should().Be(HttpStatusCode.OK);
        sessao.EmpresaId = Guid.NewGuid();
        (await Chamar(app, token)).Should().Be(HttpStatusCode.Unauthorized);
        var outroToken = Token(DateTime.UtcNow, sessao);
        sessao.DispositivoId = "outro-aparelho";
        (await Chamar(app, outroToken)).Should().Be(HttpStatusCode.Unauthorized);
        _refreshTokens.DispositivoRevogadoAsync(sessao.DispositivoId).Returns(true);
        (await Chamar(app, Token(DateTime.UtcNow, sessao))).Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── infraestrutura do teste ───────────────────────────────────────────────────────────────

    private async Task<WebApplication> SubirAsync(Dictionary<string, string?>? configuracao = null, ICacheService? cache = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = ChaveJwt,
            ["Jwt:Issuer"] = "teste",
            ["Jwt:Audience"] = "teste",
        });
        if (configuracao is not null)
            builder.Configuration.AddInMemoryCollection(configuracao);

        builder.Services.AddSingleton(_usuarios);
        builder.Services.AddSingleton(_refreshTokens);
        builder.Services.AddSingleton(cache ?? new RedisCacheService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<RedisCacheService>.Instance));
        builder.Services.AddEasyStockAuth(builder.Configuration);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet(Rota, () => "ok").RequireAuthorization();
        app.MapGet("/pair-policy", () => "ok").RequireAuthorization("Gerente");
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpStatusCode> Chamar(WebApplication app, string? token, string rota = Rota)
    {
        var endereco = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient { BaseAddress = new Uri(endereco) };
        using var pedido = new HttpRequestMessage(HttpMethod.Get, rota);
        if (token is not null) pedido.Headers.Authorization = new("Bearer", token);
        using var resposta = await http.SendAsync(pedido);
        return resposta.StatusCode;
    }

    /// <summary>JWT como o da API (sub, nivel, iat, exp, emissor e audiência), emitido em <paramref name="emitidoEm"/>.</summary>
    private string Token(DateTime emitidoEm, SessaoNativa? sessao = null, string nivel = "Admin")
    {
        List<Claim> claims = [new("sub", _usuarioId.ToString()), new("nivel", nivel)];
        if (sessao is not null) claims.AddRange([new("sid", sessao.Id.ToString()), new("dispositivoId", sessao.DispositivoId), new("empresaId", sessao.EmpresaId.ToString()), new("sessaoNativa", "true")]);
        var descritor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = emitidoEm,
            IssuedAt = emitidoEm,
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "teste",
            Audience = "teste",
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ChaveJwt)), SecurityAlgorithms.HmacSha256),
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descritor));
    }
}
