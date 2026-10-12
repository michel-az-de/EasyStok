using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Admin.VincularWhatsAppTenant;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

/// <summary>
/// #1417: conexão por coexistência. Protege a ordem (token só depois do vínculo aceito), o aviso de número fora do app
/// Business e que sincronização recusada não derruba a conexão.
/// </summary>
public class ConectarWhatsAppCoexistenciaUseCaseTests
{
    private const string Code = "code-da-meta";
    private const string Waba = "1001";
    private const string Numero = "5550001111";
    private const string Token = "EAAG-business";

    private readonly IMetaEmbeddedSignupClient _meta = Substitute.For<IMetaEmbeddedSignupClient>();
    private readonly IIntegrationCredentialResolver _credenciais = Substitute.For<IIntegrationCredentialResolver>();
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly Empresa _empresa = Empresa.Criar("Casa da Baba", "11111111000191");
    private readonly Guid _usuarioId = Guid.NewGuid();

    public ConectarWhatsAppCoexistenciaUseCaseTests()
    {
        _empresas.GetByIdAsync(_empresa.Id).Returns(_empresa);
        _meta.TrocarCodigoPorTokenAsync(Code, Arg.Any<CancellationToken>()).Returns(Token);
        _meta.ConsultarNumeroAsync(Numero, Token, Arg.Any<CancellationToken>())
            .Returns(new NumeroWhatsAppMeta("+55 11 92703-2814", "Casa da Baba", true, "CLOUD_API"));
        _meta.SolicitarSincronizacaoAsync(Numero, Token, Arg.Any<TipoSincronizacaoWhatsApp>(), Arg.Any<CancellationToken>())
            .Returns(c => $"req-{c.ArgAt<TipoSincronizacaoWhatsApp>(2)}");
    }

    private ConectarWhatsAppCoexistenciaUseCase Sut() => new(
        _meta, _credenciais,
        new VincularWhatsAppDoTenantUseCase(_empresas, _unitOfWork, new ConfigurationBuilder().Build()),
        NullLogger<ConectarWhatsAppCoexistenciaUseCase>.Instance);

    private ConectarWhatsAppCoexistenciaCommand Comando(string code = Code, string waba = Waba, string numero = Numero) =>
        new(_empresa.Id, _usuarioId, code, waba, numero);

    [Fact]
    public async Task ConectaGravaOTokenVinculaONumeroESoPedeOsContatos()
    {
        var r = await Sut().ExecuteAsync(Comando());

        r.Status.Should().Be(StatusConexaoWhatsApp.Conectado);
        r.DisplayPhoneNumber.Should().Be("+55 11 92703-2814");
        r.VerifiedName.Should().Be("Casa da Baba");
        r.IsOnBizApp.Should().BeTrue();
        r.ForaDoAppBusiness.Should().BeFalse();
        r.SincronizacaoEstadoApp.Should().Be(new SincronizacaoWhatsAppResultado(true, "req-EstadoDoApp", null));
        // Revisão da PR #1418: sem importação, pedir o history só queimaria a janela de 24 h.
        await _meta.DidNotReceive().SolicitarSincronizacaoAsync(
            Arg.Any<string>(), Arg.Any<string>(), TipoSincronizacaoWhatsApp.Historico, Arg.Any<CancellationToken>());

        _empresa.WhatsAppPhoneNumberId.Should().Be(Numero);
        await _meta.Received(1).InscreverAppNaWabaAsync(Waba, Token, Arg.Any<CancellationToken>());
        await _credenciais.Received(1).SalvarAsync(_empresa.Id, CategoriaIntegracao.Mensageria, CredencialWhatsAppMeta.ProviderKey,
            AmbienteIntegracao.Production,
            Arg.Is<CredencialWhatsAppMeta>(c => c.AccessToken == Token && c.WabaId == Waba && c.PhoneNumberId == Numero),
            _usuarioId, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CodeRecusadoPelaMetaNaoGravaNada()
    {
        _meta.TrocarCodigoPorTokenAsync(Code, Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new WhatsAppCloudException(100, "Invalid verification code format.", true, 400));

        var acao = () => Sut().ExecuteAsync(Comando());

        (await acao.Should().ThrowAsync<ConexaoWhatsAppRecusadaException>()).Which.Etapa.Should().Be(EtapaConexaoWhatsApp.TrocaDoCode);
        _empresa.WhatsAppPhoneNumberId.Should().BeNull();
        await _meta.DidNotReceiveWithAnyArgs().InscreverAppNaWabaAsync(default!, default!);
        await _credenciais.DidNotReceiveWithAnyArgs().SalvarAsync<CredencialWhatsAppMeta>(default, default, default!, default, default!, default);
    }

    [Fact]
    public async Task NumeroForaDoAppBusinessConectaMasSinaliza()
    {
        _meta.ConsultarNumeroAsync(Numero, Token, Arg.Any<CancellationToken>())
            .Returns(new NumeroWhatsAppMeta("+55 11 92703-2814", "Casa da Baba", false, "CLOUD_API"));

        var r = await Sut().ExecuteAsync(Comando());

        r.Status.Should().Be(StatusConexaoWhatsApp.Conectado);
        r.ForaDoAppBusiness.Should().BeTrue();
        _empresa.WhatsAppPhoneNumberId.Should().Be(Numero);
    }

    [Fact]
    public async Task ConsultaDoNumeroQueFalhaEErroClaroSemVincular()
    {
        _meta.ConsultarNumeroAsync(Numero, Token, Arg.Any<CancellationToken>())
            .Returns<NumeroWhatsAppMeta>(_ => throw new WhatsAppCloudException(100, "Unsupported get request.", true, 400));

        var acao = () => Sut().ExecuteAsync(Comando());

        var erro = await acao.Should().ThrowAsync<ConexaoWhatsAppRecusadaException>();
        erro.Which.Etapa.Should().Be(EtapaConexaoWhatsApp.ConsultaDoNumero);
        erro.Which.Message.Should().Contain("número");
        _empresa.WhatsAppPhoneNumberId.Should().BeNull();
    }

    [Fact]
    public async Task SincronizacaoQueFalhaNaoDerrubaAConexao()
    {
        _meta.SolicitarSincronizacaoAsync(Numero, Token, TipoSincronizacaoWhatsApp.EstadoDoApp, Arg.Any<CancellationToken>())
            .Returns<string?>(_ => throw new WhatsAppCloudException(131000, "Something went wrong", false, 500));

        var r = await Sut().ExecuteAsync(Comando());

        r.Status.Should().Be(StatusConexaoWhatsApp.Conectado);
        r.SincronizacaoEstadoApp!.Solicitada.Should().BeFalse();
        r.SincronizacaoEstadoApp.Erro.Should().Contain("Something went wrong");
        await _credenciais.ReceivedWithAnyArgs(1).SalvarAsync<CredencialWhatsAppMeta>(default, default, default!, default, default!, default);
    }

    [Fact]
    public async Task NumeroDeOutraEmpresaNaoGravaOToken()
    {
        var outra = Empresa.Criar("Outra", "22222222000191");
        outra.VincularWhatsApp(Numero);
        _empresas.GetByWhatsAppPhoneNumberIdAsync(Numero, Arg.Any<CancellationToken>()).Returns(outra);

        var r = await Sut().ExecuteAsync(Comando());

        r.Status.Should().Be(StatusConexaoWhatsApp.NumeroEmUsoPorOutraEmpresa);
        await _credenciais.DidNotReceiveWithAnyArgs().SalvarAsync<CredencialWhatsAppMeta>(default, default, default!, default, default!, default);
        await _meta.DidNotReceiveWithAnyArgs().SolicitarSincronizacaoAsync(default!, default!, default);
    }

    [Fact]
    public async Task FalhaAoGravarOTokenNaoDeixaONumeroVinculado()
    {
        // Revisão da PR #1418: vincular antes de gravar deixava a empresa com o número novo e sem token.
        _credenciais.SalvarAsync(Arg.Any<Guid>(), Arg.Any<CategoriaIntegracao>(), Arg.Any<string>(), Arg.Any<AmbienteIntegracao>(),
                Arg.Any<CredencialWhatsAppMeta>(), Arg.Any<Guid>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("KEK 'kek-x' não configurada"));

        var acao = () => Sut().ExecuteAsync(Comando());

        await acao.Should().ThrowAsync<InvalidOperationException>();
        _empresa.WhatsAppPhoneNumberId.Should().BeNull();
        await _empresas.DidNotReceive().UpdateAsync(Arg.Any<Empresa>());
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Theory]
    [InlineData("", Waba, Numero)]
    [InlineData(Code, "", Numero)]
    [InlineData(Code, Waba, "+55 11 9")]
    [InlineData(Code, "abc", Numero)]
    public async Task EntradaInvalidaERecusadaAntesDaMeta(string code, string waba, string numero)
    {
        var acao = () => Sut().ExecuteAsync(Comando(code, waba, numero));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
        await _meta.DidNotReceiveWithAnyArgs().TrocarCodigoPorTokenAsync(default!);
    }
}
