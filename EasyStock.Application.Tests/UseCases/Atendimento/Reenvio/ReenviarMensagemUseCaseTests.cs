using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Atendimento.Reenvio;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Reenvio;

/// <summary>S57 (#1355): reenvio de texto que falhou, pelo console ou pelo serviço de fundo.</summary>
public class ReenviarMensagemUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    private const string WaId = "5511982254398";

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly ICanalMensageria _whatsapp = Substitute.For<ICanalMensageria>();
    private readonly IConfiguracaoAtendimentoRepository _configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));
    private readonly Conversa _conversa;
    private readonly Mensagem _falhou;

    public ReenviarMensagemUseCaseTests()
    {
        _whatsapp.Canal.Returns(CanalConversa.WhatsApp);
        _configuracoes.GetOrDefaultAsync(_empresaId).Returns(ConfiguracaoAtendimento.CriarPadrao(_empresaId));
        _conversa = Conversa.Abrir(_empresaId, WaId, Agora.AddMinutes(-10), "Maria");
        _conversa.RegistrarEntrada(Agora.AddMinutes(-10));
        _falhou = Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Agente, Agora.AddMinutes(-5), TipoConteudoMensagem.Texto, "Temos ravioli!");
        _falhou.RegistrarFalhaEnvio("Meta fora", TipoFalhaEnvio.Temporaria, Agora.AddMinutes(-5));
        _conversas.ObterPorIdAsync(_empresaId, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_conversa);
        _conversas.ObterMensagemParaAlterarAsync(_empresaId, _conversa.Id, _falhou.Id, Arg.Any<CancellationToken>()).Returns(_falhou);
    }

    private ReenviarMensagemUseCase UseCase()
    {
        var canais = new ResolvedorCanal([_whatsapp]);
        return new(_conversas, _configuracoes, canais,
            new ReservaSmsAtendimento(canais, ReservaSmsOpcoes.Desligada, NullLogger<ReservaSmsAtendimento>.Instance), _uow, _relogio);
    }

    [Fact]
    public async Task ReenviaPeloCanalDaConversaEMarcaEnviada()
    {
        _whatsapp.EnviarTextoAsync(WaId, "Temos ravioli!", Arg.Any<CancellationToken>()).Returns("wamid.novo");

        var r = await UseCase().ExecuteAsync(_empresaId, _conversa.Id, _falhou.Id);

        r.Status.Should().Be(StatusMensagem.Enviada);
        _falhou.ExternoId.Should().Be("wamid.novo");
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task FalhaDeNovoRegistraEReagenda()
    {
        _whatsapp.EnviarTextoAsync(WaId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new WhatsAppCloudException(131000, "Meta fora", ehPermanente: false));

        var r = await UseCase().ExecuteAsync(_empresaId, _conversa.Id, _falhou.Id);

        r.Status.Should().Be(StatusMensagem.Falhou);
        _falhou.TentativasEnvio.Should().Be(2);
        _falhou.ProximoReenvioEm.Should().Be(Agora.AddMinutes(5));
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task ForaDaJanelaNaoEnviaEEncerraOReenvio()
    {
        var antiga = Conversa.Abrir(_empresaId, WaId, Agora.AddDays(-2), "Maria");
        antiga.RegistrarEntrada(Agora.AddDays(-2));
        _conversas.ObterPorIdAsync(_empresaId, _conversa.Id, Arg.Any<CancellationToken>()).Returns(antiga);

        var r = await UseCase().ExecuteAsync(_empresaId, _conversa.Id, _falhou.Id);

        r.Status.Should().Be(StatusMensagem.Falhou);
        r.Erro.Should().Contain("24 h");
        _falhou.ProximoReenvioEm.Should().BeNull();
        await _whatsapp.DidNotReceive().EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MensagemQueNaoFalhouNaoReenvia()
    {
        var ok = Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Agente, Agora, TipoConteudoMensagem.Texto, "Oi", "wamid.1");
        _conversas.ObterMensagemParaAlterarAsync(_empresaId, _conversa.Id, ok.Id, Arg.Any<CancellationToken>()).Returns(ok);

        var act = () => UseCase().ExecuteAsync(_empresaId, _conversa.Id, ok.Id);

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task MensagemInexistenteDevolveNaoEncontrada()
    {
        var act = () => UseCase().ExecuteAsync(_empresaId, _conversa.Id, Guid.NewGuid());

        await act.Should().ThrowAsync<MensagemNaoEncontradaException>();
    }
}

/// <summary>S57: o tipo da falha decide se o reenvio automático tenta de novo.</summary>
public class ClassificadorFalhaEnvioTests
{
    [Fact]
    public void ErroPermanenteDaMeta() =>
        ClassificadorFalhaEnvio.Classificar(new WhatsAppCloudException(131030, "fora da lista", ehPermanente: true))
            .Should().Be(TipoFalhaEnvio.Permanente);

    [Fact]
    public void ErroTemporarioDaMeta() =>
        ClassificadorFalhaEnvio.Classificar(new WhatsAppCloudException(131000, "erro interno", ehPermanente: false))
            .Should().Be(TipoFalhaEnvio.Temporaria);

    [Fact]
    public void RedeFora() =>
        ClassificadorFalhaEnvio.Classificar(new HttpRequestException("conexão recusada"))
            .Should().Be(TipoFalhaEnvio.Temporaria);

    [Fact]
    public void TimeoutEhIncerto() =>
        ClassificadorFalhaEnvio.Classificar(new TaskCanceledException("timeout", new TimeoutException()))
            .Should().Be(TipoFalhaEnvio.Incerta);

    [Fact]
    public void ErroDesconhecidoNaoInsiste() =>
        ClassificadorFalhaEnvio.Classificar(new InvalidOperationException("?"))
            .Should().Be(TipoFalhaEnvio.Permanente);
}

/// <summary>S57: a reserva tira o agendamento antes de reenviar, para outro processo não pegar a mesma.</summary>
public class ReservarReenviosUseCaseTests
{
    [Fact]
    public async Task TiraOAgendamentoEDevolveAsReservadas()
    {
        var agora = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        var m = Mensagem.Saida(Guid.NewGuid(), Guid.NewGuid(), AutorMensagem.Agente, agora, TipoConteudoMensagem.Texto, "Oi");
        m.RegistrarFalhaEnvio("Meta fora", TipoFalhaEnvio.Temporaria, agora);
        var repo = Substitute.For<IConversaRepository>();
        repo.ListarReenviosVencidosComLockAsync(agora, 20, Arg.Any<CancellationToken>()).Returns([m]);
        var uow = new FakeUnitOfWork();

        var r = await new ReservarReenviosUseCase(repo, uow, new FakeTimeProvider(new DateTimeOffset(agora))).ExecuteAsync(20);

        r.Should().ContainSingle().Which.Should().Be(new ReenvioReservado(m.EmpresaId, m.ConversaId, m.Id));
        m.ProximoReenvioEm.Should().BeNull();
        uow.CommitCount.Should().Be(1);
    }
}

/// <summary>S58 (#1391): fora da janela, o modelo de retomada reabre a conversa; S60: reserva por SMS.</summary>
public class ReenviarMensagemRetomadaTests
{
    private static readonly DateTime Agora = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    private const string WaId = "5511982254398";

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly ICanalMensageria _whatsapp = Substitute.For<ICanalMensageria>();
    private readonly ICanalMensageria _sms = Substitute.For<ICanalMensageria>();
    private readonly IConfiguracaoAtendimentoRepository _configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly ConfiguracaoAtendimento _configuracao;
    private readonly FakeUnitOfWork _uow = new();
    private readonly Conversa _conversa;
    private readonly Mensagem _falhou;
    private ReservaSmsOpcoes _opcoesSms = ReservaSmsOpcoes.Desligada;

    public ReenviarMensagemRetomadaTests()
    {
        _whatsapp.Canal.Returns(CanalConversa.WhatsApp);
        _sms.Canal.Returns(CanalConversa.Sms);
        _configuracao = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        _configuracoes.GetOrDefaultAsync(_empresaId).Returns(_configuracao);

        // Cliente falou há 2 dias: a janela de 24 h venceu.
        _conversa = Conversa.Abrir(_empresaId, WaId, Agora.AddDays(-2), "Maria Souza");
        _conversa.RegistrarEntrada(Agora.AddDays(-2));
        _falhou = Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Agente, Agora.AddDays(-2), TipoConteudoMensagem.Texto, "Temos ravioli!");
        _falhou.RegistrarFalhaEnvio("Meta fora", TipoFalhaEnvio.Permanente, Agora.AddDays(-2));
        _conversas.ObterPorIdAsync(_empresaId, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_conversa);
        _conversas.ObterMensagemParaAlterarAsync(_empresaId, _conversa.Id, _falhou.Id, Arg.Any<CancellationToken>()).Returns(_falhou);
        _whatsapp.EnviarModeloAsync(WaId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns("wamid.modelo");
    }

    private Task<MensagemAtendimentoResult> Reenviar()
    {
        var canais = new ResolvedorCanal([_whatsapp, _sms]);
        var useCase = new ReenviarMensagemUseCase(_conversas, _configuracoes, canais,
            new ReservaSmsAtendimento(canais, _opcoesSms, NullLogger<ReservaSmsAtendimento>.Instance),
            _uow, new FakeTimeProvider(new DateTimeOffset(Agora)));
        return useCase.ExecuteAsync(_empresaId, _conversa.Id, _falhou.Id);
    }

    [Fact]
    public async Task ComModeloEnviaARetomadaEAguardaOCliente()
    {
        _configuracao.DefinirModeloRetomada("retomar_conversa", "pt_BR");

        var r = await Reenviar();

        await _whatsapp.Received(1).EnviarModeloAsync(WaId, "retomar_conversa", "pt_BR",
            Arg.Is<IReadOnlyList<string>>(p => p.SequenceEqual(new[] { "Maria" })), Arg.Any<CancellationToken>());
        await _whatsapp.DidNotReceive().EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _conversas.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Sistema && m.ExternoId == "wamid.modelo" && m.Texto == "[modelo retomar_conversa]"),
            Arg.Any<CancellationToken>());
        _falhou.AguardaClienteDesde.Should().Be(Agora);
        r.AguardaClienteDesde.Should().Be(Agora);
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task ModeloJaEnviadoAoContatoNaoRepete()
    {
        _configuracao.DefinirModeloRetomada("retomar_conversa", "pt_BR");
        _conversas.ExisteAguardandoClienteAsync(_empresaId, CanalConversa.WhatsApp, WaId, null, Agora.AddHours(-24), Arg.Any<CancellationToken>())
            .Returns(true);

        await Reenviar();

        await _whatsapp.DidNotReceiveWithAnyArgs().EnviarModeloAsync(default!, default!, default!, default!, default);
        _falhou.AguardaClienteDesde.Should().Be(Agora);
    }

    [Fact]
    public async Task ModeloQueFalhaEncerraComOMotivo()
    {
        _configuracao.DefinirModeloRetomada("retomar_conversa", "pt_BR");
        _whatsapp.EnviarModeloAsync(WaId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new WhatsAppCloudException(132001, "modelo inexistente", ehPermanente: true));

        var r = await Reenviar();

        r.Status.Should().Be(StatusMensagem.Falhou);
        r.Erro.Should().Contain("modelo de retomada").And.Contain("modelo inexistente");
        _falhou.AguardaClienteDesde.Should().BeNull();
    }

    [Fact]
    public async Task ClienteEscreveuEmConversaNovaUsaAJanelaDela()
    {
        var nova = Conversa.Abrir(_empresaId, WaId, Agora.AddMinutes(-3), "Maria Souza");
        nova.RegistrarEntrada(Agora.AddMinutes(-3));
        _conversas.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.WhatsApp, WaId, Arg.Any<CancellationToken>()).Returns(nova);
        _whatsapp.EnviarTextoAsync(WaId, "Temos ravioli!", Arg.Any<CancellationToken>()).Returns("wamid.novo");

        var r = await Reenviar();

        r.Status.Should().Be(StatusMensagem.Enviada);
        await _whatsapp.DidNotReceiveWithAnyArgs().EnviarModeloAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task MetaRecusandoPorJanelaUsaARetomada()
    {
        _configuracao.DefinirModeloRetomada("retomar_conversa", "pt_BR");
        _conversa.RegistrarEntrada(Agora.AddHours(-1)); // pelo relógio daqui ainda há janela
        _whatsapp.EnviarTextoAsync(WaId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new WhatsAppCloudException(WhatsAppCloudException.CodigoForaDaJanela, "fora da janela", ehPermanente: true));

        await Reenviar();

        await _whatsapp.Received(1).EnviarModeloAsync(WaId, "retomar_conversa", "pt_BR", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        _falhou.AguardaClienteDesde.Should().Be(Agora);
    }

    [Fact]
    public async Task SemModeloComSmsLigadoMandaPorSms()
    {
        _opcoesSms = new ReservaSmsOpcoes(Ativa: true);
        _sms.EnviarTextoAsync(WaId, "Temos ravioli!", Arg.Any<CancellationToken>()).Returns("sms-1");

        var r = await Reenviar();

        await _sms.Received(1).EnviarTextoAsync(WaId, "Temos ravioli!", Arg.Any<CancellationToken>());
        r.ReservaSmsEm.Should().Be(Agora);
    }

    [Fact]
    public async Task SmsDesligadoNaoManda()
    {
        await Reenviar();

        await _sms.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _falhou.ReservaSmsEm.Should().BeNull();
    }

    [Fact]
    public async Task SmsNaoSaiEnquantoAguardaOCliente()
    {
        _opcoesSms = new ReservaSmsOpcoes(Ativa: true);
        _configuracao.DefinirModeloRetomada("retomar_conversa", "pt_BR");

        await Reenviar();

        await _sms.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public async Task SmsQueFalhaNaoDerrubaOReenvio()
    {
        _opcoesSms = new ReservaSmsOpcoes(Ativa: true);
        _sms.EnviarTextoAsync(WaId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("provedor fora"));

        var r = await Reenviar();

        r.Status.Should().Be(StatusMensagem.Falhou);
        r.ReservaSmsEm.Should().BeNull();
        _uow.CommitCount.Should().Be(1);
    }
}

/// <summary>S58: quem esperava o cliente e ele respondeu volta para a fila de reenvio na mesma rodada.</summary>
public class ReservarReenviosRespondidasTests
{
    [Fact]
    public async Task LiberaAsRespondidasAntesDeReservar()
    {
        var agora = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        var m = Mensagem.Saida(Guid.NewGuid(), Guid.NewGuid(), AutorMensagem.Agente, agora.AddDays(-2), TipoConteudoMensagem.Texto, "Oi");
        m.RegistrarFalhaEnvio("fora da janela", TipoFalhaEnvio.Permanente, agora.AddDays(-2));
        m.AguardarCliente(agora.AddDays(-2), "aguardando");
        var repo = Substitute.For<IConversaRepository>();
        repo.ListarAguardandoComRespostaComLockAsync(20, Arg.Any<CancellationToken>()).Returns([m]);
        repo.ListarReenviosVencidosComLockAsync(agora, 20, Arg.Any<CancellationToken>())
            .Returns(_ => m.ProximoReenvioEm is null ? [] : new List<Mensagem> { m });
        var uow = new FakeUnitOfWork();

        var r = await new ReservarReenviosUseCase(repo, uow, new FakeTimeProvider(new DateTimeOffset(agora))).ExecuteAsync(20);

        m.AguardaClienteDesde.Should().BeNull();
        r.Should().ContainSingle().Which.MensagemId.Should().Be(m.Id);
        m.ProximoReenvioEm.Should().BeNull("a reserva tira o agendamento");
    }
}
