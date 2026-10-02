using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Reenvio;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Reenvio;

/// <summary>S57 (#1355): reenvio de texto que falhou, pelo console ou pelo serviço de fundo.</summary>
public class ReenviarMensagemUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    private const string WaId = "5511982254398";

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly ICanalMensageria _whatsapp = Substitute.For<ICanalMensageria>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));
    private readonly Conversa _conversa;
    private readonly Mensagem _falhou;

    public ReenviarMensagemUseCaseTests()
    {
        _whatsapp.Canal.Returns(CanalConversa.WhatsApp);
        _conversa = Conversa.Abrir(_empresaId, WaId, Agora.AddMinutes(-10), "Maria");
        _conversa.RegistrarEntrada(Agora.AddMinutes(-10));
        _falhou = Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Agente, Agora.AddMinutes(-5), TipoConteudoMensagem.Texto, "Temos ravioli!");
        _falhou.RegistrarFalhaEnvio("Meta fora", TipoFalhaEnvio.Temporaria, Agora.AddMinutes(-5));
        _conversas.ObterPorIdAsync(_empresaId, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_conversa);
        _conversas.ObterMensagemParaAlterarAsync(_empresaId, _conversa.Id, _falhou.Id, Arg.Any<CancellationToken>()).Returns(_falhou);
    }

    private ReenviarMensagemUseCase UseCase() => new(_conversas, new ResolvedorCanal([_whatsapp]), _uow, _relogio);

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
