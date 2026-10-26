using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

public class GerenciarWppConnectUseCaseTests
{
    private readonly IWppConnectSessaoClient _cliente = Substitute.For<IWppConnectSessaoClient>();
    private readonly IIntegrationCredentialResolver _credenciais = Substitute.For<IIntegrationCredentialResolver>();
    private readonly ITenantFeatureFlagRepository _flags = Substitute.For<ITenantFeatureFlagRepository>();
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly Guid _empresa = Guid.NewGuid();
    private GerenciarWppConnectUseCase UseCase => new(_cliente, _credenciais, _flags, _empresas);

    public GerenciarWppConnectUseCaseTests()
    {
        _cliente.Habilitado.Returns(true);
        _empresas.GetByIdAsync(_empresa).Returns(Empresa.Criar("Piloto", "11111111000191"));
        _flags.ListarAtivasAsync(_empresa, Arg.Any<CancellationToken>()).Returns(new[] { FeatureCatalogo.ModuloAtendimento });
        _cliente.ObterEstadoAsync(Arg.Any<CredencialWppConnect>(), Arg.Any<CancellationToken>())
            .Returns(new EstadoWppConnect(true, true, false, "QRCODE"));
    }

    [Fact]
    public async Task Conexao_deriva_sessao_da_empresa_e_salva_credencial_antes_de_iniciar()
    {
        var nome = CredencialWppConnect.NomeSessao(_empresa);
        _cliente.GerarTokenAsync(nome, Arg.Any<CancellationToken>()).Returns("token-sessao");
        var usuario = Guid.NewGuid();
        await UseCase.ConectarAsync(_empresa, usuario);
        Received.InOrder(() =>
        {
            _credenciais.SalvarAsync(_empresa, CategoriaIntegracao.Mensageria, CredencialWppConnect.ProviderKey,
                AmbienteIntegracao.Production, Arg.Is<CredencialWppConnect>(s => s.Sessao == nome && s.Token == "token-sessao" && s.InicioDaCaptura != default), usuario,
                ct: Arg.Any<CancellationToken>());
            _cliente.IniciarAsync(Arg.Is<CredencialWppConnect>(s => s.Sessao == nome && s.Token == "token-sessao"), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Credencial_de_outra_empresa_nao_e_operada()
    {
        var outra = new CredencialWppConnect(CredencialWppConnect.NomeSessao(Guid.NewGuid()), "token-outra");
        _credenciais.ObterAsync<CredencialWppConnect>(_empresa, CredencialWppConnect.ProviderKey,
            AmbienteIntegracao.Production, Arg.Any<CancellationToken>()).Returns(outra);
        var act = () => UseCase.ConectarAsync(_empresa, Guid.NewGuid());
        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _cliente.DidNotReceiveWithAnyArgs().IniciarAsync(default!, default);
    }

    [Fact]
    public async Task Empresa_sem_modulo_nao_gera_sessao()
    {
        _flags.ListarAtivasAsync(_empresa, Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        var act = () => UseCase.ConectarAsync(_empresa, Guid.NewGuid());
        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _cliente.DidNotReceiveWithAnyArgs().GerarTokenAsync(default!, default);
    }

    [Fact]
    public async Task Reconectar_preserva_token_e_fecha_sem_logout()
    {
        var sessao = new CredencialWppConnect(CredencialWppConnect.NomeSessao(_empresa), "token-atual", DateTimeOffset.UtcNow);
        _cliente.ObterEstadoAsync(sessao, Arg.Any<CancellationToken>())
            .Returns(new EstadoWppConnect(true, true, true, "CONNECTED"));
        _credenciais.ObterAsync<CredencialWppConnect>(_empresa, CredencialWppConnect.ProviderKey,
            AmbienteIntegracao.Production, Arg.Any<CancellationToken>()).Returns(sessao);
        await UseCase.ReconectarAsync(_empresa, Guid.NewGuid());
        Received.InOrder(() =>
        {
            _cliente.FecharAsync(sessao, false, Arg.Any<CancellationToken>());
            _cliente.IniciarAsync(sessao, Arg.Any<CancellationToken>());
        });
        await _cliente.DidNotReceiveWithAnyArgs().GerarTokenAsync(default!, default);
        await _credenciais.DidNotReceiveWithAnyArgs().DesativarAsync(default, default!, default);
    }

    [Theory]
    [InlineData("QRCODE")]
    [InlineData("INITIALIZING")]
    public async Task Atualizar_qr_nao_abandona_browser_ainda_em_inicializacao(string estado)
    {
        var sessao = new CredencialWppConnect(CredencialWppConnect.NomeSessao(_empresa), "token-atual", DateTimeOffset.UtcNow);
        _credenciais.ObterAsync<CredencialWppConnect>(_empresa, CredencialWppConnect.ProviderKey,
            AmbienteIntegracao.Production, Arg.Any<CancellationToken>()).Returns(sessao);
        _cliente.ObterEstadoAsync(sessao, Arg.Any<CancellationToken>())
            .Returns(new EstadoWppConnect(true, true, false, estado));
        (await UseCase.ReconectarAsync(_empresa, Guid.NewGuid())).Estado.Should().Be(estado);
        await _cliente.DidNotReceiveWithAnyArgs().FecharAsync(default!, default, default);
        await _cliente.DidNotReceiveWithAnyArgs().IniciarAsync(default!, default);
    }

    [Fact]
    public async Task Desconectar_desativa_apenas_o_provider_da_empresa_corrente()
    {
        await UseCase.DesconectarAsync(_empresa);
        await _credenciais.Received(1).DesativarAsync(_empresa, "wppconnect", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Servico_desabilitado_nao_consulta_WhatsApp()
    {
        _cliente.Habilitado.Returns(false);
        (await UseCase.ObterAsync(_empresa)).Habilitado.Should().BeFalse();
        await _cliente.DidNotReceiveWithAnyArgs().ObterEstadoAsync(default!, default);
    }
}
