using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
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
    public async Task AutenticacaoDeProducaoRegistraOValidadorNoJwtBearer()
    {
        // Guarda do cabeamento: sem o OnTokenValidated, nenhum teste acima passaria a existir em produção.
        await using var app = await SubirAsync();
        var opcoes = app.Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>()
            .Get(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme);

        opcoes.Events.OnTokenValidated.Should().NotBeNull();
        opcoes.MapInboundClaims.Should().BeFalse("o validador lê sub e iat com o nome do JWT");
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
        builder.Services.AddSingleton(cache ?? new RedisCacheService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<RedisCacheService>.Instance));
        builder.Services.AddEasyStockAuth(builder.Configuration);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet(Rota, () => "ok").RequireAuthorization();
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpStatusCode> Chamar(WebApplication app, string? token)
    {
        var endereco = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient { BaseAddress = new Uri(endereco) };
        using var pedido = new HttpRequestMessage(HttpMethod.Get, Rota);
        if (token is not null) pedido.Headers.Authorization = new("Bearer", token);
        using var resposta = await http.SendAsync(pedido);
        return resposta.StatusCode;
    }

    /// <summary>JWT como o da API (sub, nivel, iat, exp, emissor e audiência), emitido em <paramref name="emitidoEm"/>.</summary>
    private string Token(DateTime emitidoEm)
    {
        var descritor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", _usuarioId.ToString()), new Claim("nivel", "Admin")]),
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
