using System.Security.Claims;
using EasyStock.Api.Configuration;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Integration.Conexao;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Integracoes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>F16 (#1246): o PUT sem KEK responde 503 com o motivo, e o rate limit do Testar é por loja e provider.</summary>
public class IntegracoesControllerTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ICredencialIntegracaoRepository _credenciais = Substitute.For<ICredencialIntegracaoRepository>();
    private readonly IIntegrationCredentialResolver _resolver = Substitute.For<IIntegrationCredentialResolver>();
    private readonly IChavesGlobaisIntegracao _globais = Substitute.For<IChavesGlobaisIntegracao>();
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private IntegracoesController Controller()
    {
        _currentUser.EmpresaId.Returns(_empresaId);
        _currentUser.UsuarioId.Returns(Guid.NewGuid());
        _credenciais.ListarAtivasDoProviderAsync(default, default!, default).ReturnsForAnyArgs([]);
        _credenciais.ListarAtivasDaEmpresaAsync(default, default).ReturnsForAnyArgs([]);
        var estado = new EstadoTesteIntegracaoEmMemoria();
        var executor = new ExecutorTesteIntegracao([], _credenciais, _resolver, _globais, estado, _empresas,
            Substitute.For<IUnitOfWork>(), TimeProvider.System, NullLogger<ExecutorTesteIntegracao>.Instance);
        return new IntegracoesController(
            new ListarIntegracoesUseCase(_credenciais, _globais, estado, _empresas, TimeProvider.System),
            new SalvarChaveIntegracaoUseCase(_credenciais, _resolver),
            new TestarIntegracaoUseCase(executor),
            new DesativarIntegracaoUseCase(_resolver),
            _currentUser);
    }

    [Fact]
    public async Task SalvarSemKekResponde503ComOMotivo()
    {
        _resolver.SalvarAsync(default, default, default!, default, default(Dictionary<string, string>)!, default)
            .ThrowsAsyncForAnyArgs(new ChaveMestraAusenteException("sem KEK"));

        var resultado = await Controller().Salvar("mercadopago",
            new SalvarChaveIntegracaoBody(new() { ["accessToken"] = "APP_USR-token-de-teste" }, null, null), CancellationToken.None);

        var objeto = resultado.Should().BeOfType<ObjectResult>().Subject;
        objeto.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        System.Text.Json.JsonSerializer.Serialize(objeto.Value).Should().Contain("EZ_CRYPTO_KEK").And.NotContain("APP_USR");
    }

    [Fact]
    public async Task ProviderDesconhecidoResponde404()
    {
        var resultado = await Controller().Testar("pagseguro", CancellationToken.None);

        resultado.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void RateLimitDoTestarSeparaLojaEProvider()
    {
        static HttpContext Contexto(string empresa, string provider)
        {
            var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("empresaId", empresa)], "teste")) };
            ctx.Request.RouteValues["provider"] = provider;
            return ctx;
        }

        var a = IntegracaoTesteRateLimit.Chave(Contexto("empresa-a", "MercadoPago"));

        a.Should().Be(IntegracaoTesteRateLimit.Chave(Contexto("empresa-a", "mercadopago")));
        a.Should().NotBe(IntegracaoTesteRateLimit.Chave(Contexto("empresa-b", "mercadopago")));
        a.Should().NotBe(IntegracaoTesteRateLimit.Chave(Contexto("empresa-a", "lalamove")));
        IntegracaoTesteRateLimit.PorMinuto.Should().Be(6);
    }
}
