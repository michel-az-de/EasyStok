using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Admin.VincularWhatsAppTenant;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Notifications.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>#1417: endpoints da coexistência. Status HTTP por desfecho e config pública sem segredo.</summary>
public class IntegracoesWhatsAppCoexistenciaControllerTests
{
    private const string Token = "EAAG-business";
    private readonly IMetaEmbeddedSignupClient _meta = Substitute.For<IMetaEmbeddedSignupClient>();
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly Empresa _empresa = Empresa.Criar("Casa da Baba", "11111111000191");

    public IntegracoesWhatsAppCoexistenciaControllerTests()
    {
        _currentUser.EmpresaId.Returns(_empresa.Id);
        _currentUser.UsuarioId.Returns(Guid.NewGuid());
        _empresas.GetByIdAsync(_empresa.Id).Returns(_empresa);
        _meta.TrocarCodigoPorTokenAsync("code", Arg.Any<CancellationToken>()).Returns(Token);
        _meta.ConsultarNumeroAsync("555", Token, Arg.Any<CancellationToken>())
            .Returns(new NumeroWhatsAppMeta("+55 11 92703-2814", "Casa da Baba", true, "CLOUD_API"));
    }

    private static readonly Dictionary<string, string?> ComKek = new()
    {
        ["Crypto:CurrentKekId"] = "kek-teste",
        ["Crypto:Keks:kek-teste"] = Convert.ToBase64String(new byte[32])
    };

    private static IntegracoesWhatsAppController Controller(
        MetaCloudWhatsAppOptions? meta = null, ICurrentUserAccessor? usuario = null, Dictionary<string, string?>? config = null) =>
        new(new ObterStatusIntegracaoWhatsAppUseCase(Substitute.For<ITenantFeatureFlagRepository>(), Substitute.For<IEmpresaRepository>(),
                Substitute.For<IConfiguracaoAtendimentoRepository>()),
            Options.Create(meta ?? new MetaCloudWhatsAppOptions()),
            new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build(),
            usuario ?? Substitute.For<ICurrentUserAccessor>());

    private static MetaCloudWhatsAppOptions MetaCompleta() => new()
    {
        AppId = "897859909924304", EmbeddedSignupConfigId = "cfg-1", AppSecret = "segredo", ApiVersion = "v26.0"
    };

    private ConectarWhatsAppCoexistenciaUseCase UseCase() => new(
        _meta, Substitute.For<IIntegrationCredentialResolver>(),
        new VincularWhatsAppDoTenantUseCase(_empresas, Substitute.For<IUnitOfWork>(), new ConfigurationBuilder().Build()),
        NullLogger<ConectarWhatsAppCoexistenciaUseCase>.Instance);

    private static object? Campo(IActionResult resultado, string nome)
    {
        var valor = ((ObjectResult)resultado).Value!;
        var data = valor.GetType().GetProperty("Data")!.GetValue(valor)!;
        return data.GetType().GetProperty(nome)!.GetValue(data);
    }

    [Fact]
    public void ConfigCompletaFicaHabilitadaSemExporOSegredo()
    {
        var resultado = Controller(MetaCompleta(), config: ComKek).GetConfigCoexistencia();

        Campo(resultado, "appId").Should().Be("897859909924304");
        Campo(resultado, "configId").Should().Be("cfg-1");
        Campo(resultado, "graphVersion").Should().Be("v26.0");
        Campo(resultado, "habilitado").Should().Be(true);
        ((ObjectResult)resultado).Value!.ToString().Should().NotContain("segredo");
    }

    [Fact]
    public void SemKekFicaDesabilitadaParaNaoPerderOTokenDepoisDaMeta()
    {
        var resultado = Controller(MetaCompleta()).GetConfigCoexistencia();

        Campo(resultado, "habilitado").Should().Be(false);
    }

    [Fact]
    public void SemConfigIdFicaDesabilitada()
    {
        var resultado = Controller(new MetaCloudWhatsAppOptions { AppId = "1", AppSecret = "s" }).GetConfigCoexistencia();

        Campo(resultado, "habilitado").Should().Be(false);
    }

    [Fact]
    public async Task ConexaoComSucessoDevolveONumero()
    {
        var resultado = await Controller(usuario: _currentUser).PostCoexistencia(
            new ConectarWhatsAppCoexistenciaRequest("code", "1001", "555"), UseCase(), CancellationToken.None);

        resultado.Should().BeOfType<OkObjectResult>();
        Campo(resultado, "displayPhoneNumber").Should().Be("+55 11 92703-2814");
        Campo(resultado, "isOnBizApp").Should().Be(true);
        Campo(resultado, "foraDoAppBusiness").Should().Be(false);
    }

    [Fact]
    public async Task NumeroForaDoAppBusinessConectaComAviso()
    {
        _meta.ConsultarNumeroAsync("555", Token, Arg.Any<CancellationToken>())
            .Returns(new NumeroWhatsAppMeta("+55 11 92703-2814", "Casa da Baba", false, "CLOUD_API"));

        var resultado = await Controller(usuario: _currentUser).PostCoexistencia(
            new ConectarWhatsAppCoexistenciaRequest("code", "1001", "555"), UseCase(), CancellationToken.None);

        resultado.Should().BeOfType<OkObjectResult>();
        Campo(resultado, "foraDoAppBusiness").Should().Be(true);
    }

    [Fact]
    public async Task CodeInvalidoE400()
    {
        _meta.TrocarCodigoPorTokenAsync("code", Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new WhatsAppCloudException(100, "Invalid verification code format.", true, 400));

        var resultado = await Controller(usuario: _currentUser).PostCoexistencia(
            new ConectarWhatsAppCoexistenciaRequest("code", "1001", "555"), UseCase(), CancellationToken.None);

        resultado.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<ApiErrorResponse>()
            .Which.Error.Message.Should().Contain("código");
    }

    [Fact]
    public async Task RecusaDaMetaDepoisDoCodeE502()
    {
        _meta.InscreverAppNaWabaAsync("1001", Token, Arg.Any<CancellationToken>())
            .Returns(_ => throw new WhatsAppCloudException(200, "Permissions error", true, 403));

        var resultado = await Controller(usuario: _currentUser).PostCoexistencia(
            new ConectarWhatsAppCoexistenciaRequest("code", "1001", "555"), UseCase(), CancellationToken.None);

        ((ObjectResult)resultado).StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task TimeoutNaTrocaDoCodeE502SemSegredo()
    {
        // Revisão da PR #1418: rede e tempo esgotado viravam 500. A Meta não respondeu: não é culpa do code.
        _meta.TrocarCodigoPorTokenAsync("code", Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout",
                new TimeoutException()));

        var resultado = await Controller(usuario: _currentUser).PostCoexistencia(
            new ConectarWhatsAppCoexistenciaRequest("code", "1001", "555"), UseCase(), CancellationToken.None);

        ((ObjectResult)resultado).StatusCode.Should().Be(502);
        var erro = ((ApiErrorResponse)((ObjectResult)resultado).Value!).Error;
        erro.Message.Should().Contain("não respondeu");
        erro.Message.Should().NotContain("code=").And.NotContain("client_secret");
    }

    [Fact]
    public async Task FalhaDeRedeDepoisDoCodeE502SemSegredo()
    {
        _meta.InscreverAppNaWabaAsync("1001", Token, Arg.Any<CancellationToken>())
            .Returns(_ => throw new HttpRequestException(
                "No such host is known. (graph.facebook.com:443) access_token=" + Token));

        var resultado = await Controller(usuario: _currentUser).PostCoexistencia(
            new ConectarWhatsAppCoexistenciaRequest("code", "1001", "555"), UseCase(), CancellationToken.None);

        ((ObjectResult)resultado).StatusCode.Should().Be(502);
        var erro = ((ApiErrorResponse)((ObjectResult)resultado).Value!).Error;
        (erro.Message + erro.Detail).Should().NotContain(Token);
    }
}
