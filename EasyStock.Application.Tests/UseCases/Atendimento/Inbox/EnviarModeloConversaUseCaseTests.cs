using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Inbox;

public class EnviarModeloConversaUseCaseTests
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IConsentimentoContatoRepository _consentimentos = Substitute.For<IConsentimentoContatoRepository>();
    private readonly ICatalogoModelosAtendimento _catalogo = Substitute.For<ICatalogoModelosAtendimento>();
    private readonly ICanalMensageria _canal = Substitute.For<ICanalMensageria>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly List<Mensagem> _mensagens = [];
    private readonly Conversa _conversa;
    private readonly Cliente _cliente;
    private EnviarModeloConversaCommand Command => new(_empresa, _usuario, _conversa.Id, Guid.NewGuid(), "retomar", "pt_BR", ["Ana"]);
    private EnviarModeloConversaUseCase UseCase => new(_conversas, _clientes, _catalogo, new(_consentimentos), new([_canal]), _uow, TimeProvider.System);

    public EnviarModeloConversaUseCaseTests()
    {
        _cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = _empresa, Nome = "Ana" };
        _conversa = Conversa.Abrir(_empresa, "5511999990001", DateTime.UtcNow.AddDays(-2), clienteId: _cliente.Id);
        _conversa.RegistrarEntrada(DateTime.UtcNow.AddDays(-2));
        _conversas.ObterPorIdAsync(_empresa, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_conversa);
        _conversas.ObterPorIdComLockAsync(_empresa, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_conversa);
        _clientes.GetByIdAsync(_empresa, _cliente.Id).Returns(_cliente);
        _conversas.ObterMensagemAsync(_empresa, _conversa.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _mensagens.FirstOrDefault(m => m.Id == c.ArgAt<Guid>(2)));
        _conversas.ObterMensagemComLockAsync(_empresa, _conversa.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _mensagens.FirstOrDefault(m => m.Id == c.ArgAt<Guid>(2)));
        _conversas.AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>()).Returns(c =>
        { _mensagens.Add(c.Arg<Mensagem>()); return Task.CompletedTask; });
        _canal.Canal.Returns(CanalConversa.WhatsApp);
        _canal.EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => { _uow.CommitCount.Should().BeGreaterThan(0); return "wamid.modelo"; });
        Aprovar();
    }

    private void Aprovar(string categoria = "UTILITY", string? indisponivel = null) =>
        _catalogo.ListarAsync(_empresa, Arg.Any<CancellationToken>()).Returns([
            new ModeloAtendimento("retomar", "pt_BR", categoria, "Olá {{1}}, vamos continuar? {{1}}", 1, "Casa", "Até logo", indisponivel)]);

    [Fact]
    public async Task ForaDaJanela_GravaAntesDoCanal_AutoriaPreviaEPreservacaoDaJanela()
    {
        var entrada = _conversa.UltimaMensagemEntradaEm;
        var command = Command;
        var m = await UseCase.ExecuteAsync(command);
        m.Id.Should().Be(command.TentativaId);
        m.Status.Should().Be(StatusMensagem.Enviada);
        m.Texto.Should().Be("Casa\nOlá Ana, vamos continuar? Ana\nAté logo");
        m.EnviadaPorUsuarioId.Should().Be(_usuario);
        m.Modelo!.Nome.Should().Be("retomar");
        _conversa.AssumidaPorUsuarioId.Should().Be(_usuario);
        _conversa.UltimaMensagemEntradaEm.Should().Be(entrada);
        _conversa.DentroDaJanela(DateTime.UtcNow).Should().BeFalse();
        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        await UseCase.ExecuteAsync(command);
        _mensagens.Should().ContainSingle();
        await _canal.Received(1).EnviarModeloAsync(Arg.Any<string>(), "retomar", "pt_BR", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("nome")]
    [InlineData("idioma")]
    [InlineData("parametros")]
    [InlineData("usuario")]
    public async Task MesmaTentativaComOutroConteudoOuAutor_NaoReenvia(string alteracao)
    {
        var command = Command;
        await UseCase.ExecuteAsync(command);
        var outro = alteracao switch {
            "nome" => command with { Nome = "outro" }, "idioma" => command with { Idioma = "en_US" },
            "parametros" => command with { Parametros = ["Outro"] }, _ => command with { UsuarioId = Guid.NewGuid() } };
        var act = () => UseCase.ExecuteAsync(outro);
        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData("encerrada")]
    [InlineData("bloqueado")]
    [InlineData("revogado")]
    [InlineData("marketing")]
    [InlineData("semCliente")]
    [InlineData("outraEmpresa")]
    [InlineData("naoAprovado")]
    [InlineData("idioma")]
    [InlineData("quantidade")]
    [InlineData("formato")]
    [InlineData("vazio")]
    public async Task RecusasAntesDoEnvio(string caso)
    {
        var command = Command;
        switch (caso)
        {
            case "encerrada": _conversa.Encerrar(DateTime.UtcNow); break;
            case "bloqueado": _cliente.Bloquear("motivo", DateTime.UtcNow); break;
            case "revogado": _consentimentos.ListarDoClienteAsync(_empresa, _cliente.Id, Arg.Any<CancellationToken>()).Returns([
                ConsentimentoContato.Registrar(_empresa, _cliente.Id, CanalConversa.WhatsApp, FinalidadeContato.Transacional,
                    SituacaoConsentimento.Revogado, "teste", DateTime.UtcNow)]); break;
            case "marketing": Aprovar("MARKETING"); break;
            case "semCliente": _clientes.GetByIdAsync(_empresa, _cliente.Id).Returns((Cliente?)null); break;
            case "outraEmpresa": _conversas.ObterPorIdAsync(_empresa, _conversa.Id, Arg.Any<CancellationToken>())
                .Returns(Conversa.Abrir(Guid.NewGuid(), "5511999990002", DateTime.UtcNow)); break;
            case "naoAprovado": _catalogo.ListarAsync(_empresa, Arg.Any<CancellationToken>()).Returns([]); break;
            case "idioma": command = command with { Idioma = "en_US" }; break;
            case "quantidade": command = command with { Parametros = [] }; break;
            case "formato": Aprovar(indisponivel: "mídia não aceita"); break;
            case "vazio": command = command with { Parametros = [" "] }; break;
        }
        var act = () => UseCase.ExecuteAsync(command);
        await act.Should().ThrowAsync<Exception>();
        _mensagens.Should().BeEmpty();
        _uow.CommitCount.Should().Be(0);
        await _canal.DidNotReceiveWithAnyArgs().EnviarModeloAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task SoCanalComAceitaModelo()
    {
        var conversa = Conversa.Abrir(_empresa, "sessao", DateTime.UtcNow, canal: CanalConversa.ChatSite);
        _conversas.ObterPorIdAsync(_empresa, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        var act = () => UseCase.ExecuteAsync(Command with { ConversaId = conversa.Id });
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*canal não aceita*");
        _catalogo.ReceivedCalls().Should().BeEmpty();
    }

    // #1550: recusa levantada antes da rede (empresa sem phone_number_id) é permanente e nada saiu. O "2xx sem
    // message id" continua incerto (a Meta respondeu e algo pode ter saído), assim como o 5xx.
    [Theory]
    [InlineData(true, null, TipoFalhaEnvio.Permanente)]
    [InlineData(true, 400, TipoFalhaEnvio.Permanente)]
    [InlineData(false, 429, TipoFalhaEnvio.Permanente)]
    [InlineData(true, 200, TipoFalhaEnvio.Incerta)]
    [InlineData(false, 503, TipoFalhaEnvio.Incerta)]
    [InlineData(false, null, TipoFalhaEnvio.Incerta)]
    public async Task FalhaDoCanal_ClassificaPeloQuePodeTerSaido(bool ehPermanente, int? statusHttp, TipoFalhaEnvio esperado)
    {
        _canal.EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new WhatsAppCloudException(0, "falha", ehPermanente, statusHttp));
        var m = await UseCase.ExecuteAsync(Command);
        m.Status.Should().Be(StatusMensagem.Falhou);
        _mensagens.Single().UltimaFalhaEnvio.Should().Be(esperado);
        (m.Erro!.Contains("incerto")).Should().Be(esperado == TipoFalhaEnvio.Incerta);
        _mensagens.Single().ProximoReenvioEm.Should().BeNull();
    }

    [Fact]
    public async Task Timeout_ConservaIntencao_NaoAgendaReenvioNemSms_NemRepeteAoRetomar()
    {
        _canal.EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new TimeoutException());
        var command = Command;
        var m = await UseCase.ExecuteAsync(command);
        m.Status.Should().Be(StatusMensagem.Falhou);
        m.Erro.Should().Contain("incerto");
        _mensagens.Single().PodeReenviar.Should().BeFalse();
        _mensagens.Single().ProximoReenvioEm.Should().BeNull();
        _mensagens.Single().PrecisaDeReservaSms.Should().BeFalse();
        await UseCase.ExecuteAsync(command);
        await _canal.Received(1).EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // #1599: a varredura de intenção presa marcou falha incerta com o POST ainda no ar, e o canal confirmou
    // depois. A entrega vence, e nada do que a varredura gravou fica pendurado na mensagem enviada.
    [Fact]
    public async Task CanalConfirmaDepoisDaVarreduraMarcarFalhaIncerta_SucessoVenceELimpaAFalha()
    {
        _canal.EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => { VarreduraMarcaFalhaIncerta(); return "wamid.tardio"; });
        var m = await UseCase.ExecuteAsync(Command);
        m.Status.Should().Be(StatusMensagem.Enviada);
        m.ExternoId.Should().Be("wamid.tardio");
        m.Erro.Should().BeNull();
        var salva = _mensagens.Single();
        salva.TentativasEnvio.Should().Be(0);
        salva.UltimaFalhaEnvio.Should().BeNull();
        salva.ProximoReenvioEm.Should().BeNull();
    }

    // #1599: a varredura já encerrou a intenção e o canal falhou depois. A falha conta uma vez só.
    [Fact]
    public async Task CanalFalhaDepoisDaVarreduraMarcarFalhaIncerta_NaoSomaOutraFalha()
    {
        _canal.EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => { VarreduraMarcaFalhaIncerta(); throw new TimeoutException(); });
        var m = await UseCase.ExecuteAsync(Command);
        m.Status.Should().Be(StatusMensagem.Falhou);
        _mensagens.Single().TentativasEnvio.Should().Be(1);
        _mensagens.Single().UltimaFalhaEnvio.Should().Be(TipoFalhaEnvio.Incerta);
    }

    private void VarreduraMarcaFalhaIncerta() =>
        _mensagens.Single().RegistrarFalhaEnvio(EnviarModeloConversaUseCase.AvisoEnvioIncerto, TipoFalhaEnvio.Incerta, DateTime.UtcNow);
}
