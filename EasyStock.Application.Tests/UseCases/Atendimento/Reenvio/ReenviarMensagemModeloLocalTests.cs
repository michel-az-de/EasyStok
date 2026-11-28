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

/// <summary>
/// #3415: na sessão web do WhatsApp não há modelo da Meta; o modelo de retomada é a mensagem padrão local e sai como texto,
/// com a mesma regra de 7 dias do envio manual.
/// </summary>
public class ReenviarMensagemModeloLocalTests
{
    private static readonly DateTime Agora = new(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);
    private const string WaId = "5511982254398";

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly ICanalMensageria _whatsapp = Substitute.For<ICanalMensageria>();
    private readonly IConfiguracaoAtendimentoRepository _configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly ICatalogoModelosAtendimento _catalogo = Substitute.For<ICatalogoModelosAtendimento>();
    private readonly FakeUnitOfWork _uow = new();

    public ReenviarMensagemModeloLocalTests()
    {
        _whatsapp.Canal.Returns(CanalConversa.WhatsApp);
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        configuracao.DefinirModeloRetomada("retomada_conversa", "pt_BR");
        _configuracoes.GetOrDefaultAsync(_empresaId).Returns(configuracao);
        _catalogo.UsaModelosLocaisAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(true);
        _catalogo.ListarAsync(_empresaId, Arg.Any<CancellationToken>()).Returns([
            new ModeloAtendimento("retomada_conversa", "pt_BR", "UTILITY", "Oi {{1}}, podemos continuar seu pedido?", 1,
                "Casa da Baba", null, null, OrigemModelo.Local)]);
        _whatsapp.EnviarTextoAsync(WaId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("wpp.retomada");
    }

    private (Conversa Conversa, Mensagem Falhou) FalhaDeQuemEscreveuHa(TimeSpan tempo)
    {
        var conversa = Conversa.Abrir(_empresaId, WaId, Agora - tempo, "Maria Souza");
        conversa.RegistrarEntrada(Agora - tempo);
        var falhou = Mensagem.Saida(_empresaId, conversa.Id, AutorMensagem.Agente, Agora - tempo, TipoConteudoMensagem.Texto, "Temos ravioli!");
        falhou.RegistrarFalhaEnvio("Sessão fora", TipoFalhaEnvio.Permanente, Agora - tempo);
        _conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        _conversas.ObterMensagemComLockAsync(_empresaId, conversa.Id, falhou.Id, Arg.Any<CancellationToken>()).Returns(falhou);
        return (conversa, falhou);
    }

    private Task<MensagemAtendimentoResult> Reenviar(Conversa conversa, Mensagem falhou)
    {
        var canais = new ResolvedorCanal([_whatsapp]);
        var useCase = new ReenviarMensagemUseCase(_conversas, _configuracoes, canais,
            new ReservaSmsAtendimento(canais, ReservaSmsOpcoes.Desligada, NullLogger<ReservaSmsAtendimento>.Instance),
            _uow, new FakeTimeProvider(new DateTimeOffset(Agora)), _catalogo);
        return useCase.ExecuteAsync(_empresaId, conversa.Id, falhou.Id);
    }

    [Fact]
    public async Task ModeloLocalSaiComoTextoPreenchidoEAguardaOCliente()
    {
        var (conversa, falhou) = FalhaDeQuemEscreveuHa(TimeSpan.FromDays(2));

        var r = await Reenviar(conversa, falhou);

        await _whatsapp.Received(1).EnviarTextoAsync(WaId, "Casa da Baba\nOi Maria, podemos continuar seu pedido?", Arg.Any<CancellationToken>());
        await _whatsapp.DidNotReceiveWithAnyArgs().EnviarModeloAsync(default!, default!, default!, default!, default);
        await _conversas.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Sistema && m.ExternoId == "wpp.retomada"), Arg.Any<CancellationToken>());
        r.AguardaClienteDesde.Should().Be(Agora);
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task ClienteQueEscreveuHaMaisDe7DiasNaoRecebeEFalhaComMotivo()
    {
        var (conversa, falhou) = FalhaDeQuemEscreveuHa(TimeSpan.FromDays(8));

        var r = await Reenviar(conversa, falhou);

        r.Status.Should().Be(StatusMensagem.Falhou);
        r.Erro.Should().Contain("7 dias");
        falhou.ProximoReenvioEm.Should().BeNull();
        await _whatsapp.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public async Task ModeloForaDoCatalogoNaoEnviaEFalhaComMotivo()
    {
        _catalogo.ListarAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(Array.Empty<ModeloAtendimento>());
        var (conversa, falhou) = FalhaDeQuemEscreveuHa(TimeSpan.FromDays(2));

        var r = await Reenviar(conversa, falhou);

        r.Status.Should().Be(StatusMensagem.Falhou);
        r.Erro.Should().Contain("retomada_conversa");
        await _whatsapp.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        await _whatsapp.DidNotReceiveWithAnyArgs().EnviarModeloAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task EmpresaNaMetaSegueComOModeloDaMetaSemListarOCatalogo()
    {
        _catalogo.UsaModelosLocaisAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(false);
        _whatsapp.EnviarModeloAsync(WaId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns("wamid.modelo");
        var (conversa, falhou) = FalhaDeQuemEscreveuHa(TimeSpan.FromDays(2));

        await Reenviar(conversa, falhou);

        await _whatsapp.Received(1).EnviarModeloAsync(WaId, "retomada_conversa", "pt_BR",
            Arg.Is<IReadOnlyList<string>>(p => p.SequenceEqual(new[] { "Maria" })), Arg.Any<CancellationToken>());
        await _catalogo.DidNotReceiveWithAnyArgs().ListarAsync(default, default);
    }

    [Fact]
    public async Task ClienteQueEscreveuNumaConversaMaisNovaRecebe()
    {
        // A mensagem é de uma conversa de 8 dias; o cliente voltou há 30 h numa conversa aberta (fora das 24 h, dentro dos 7 dias).
        var (conversa, falhou) = FalhaDeQuemEscreveuHa(TimeSpan.FromDays(8));
        var aberta = Conversa.Abrir(_empresaId, WaId, Agora.AddHours(-30), "Maria Souza");
        aberta.RegistrarEntrada(Agora.AddHours(-30));
        _conversas.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.WhatsApp, WaId, Arg.Any<CancellationToken>()).Returns(aberta);

        await Reenviar(conversa, falhou);

        await _whatsapp.Received(1).EnviarTextoAsync(WaId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContatoQuePediuParaPararNaoRecebe()
    {
        var (conversa, falhou) = FalhaDeQuemEscreveuHa(TimeSpan.FromDays(2));
        _conversas.ListarMensagensAsync(_empresaId, conversa.Id, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Mensagem.Entrada(_empresaId, conversa.Id, Agora.AddDays(-2), TipoConteudoMensagem.Texto, "SAIR")]);

        var r = await Reenviar(conversa, falhou);

        r.Status.Should().Be(StatusMensagem.Falhou);
        r.Erro.Should().Contain("parar");
        await _whatsapp.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public async Task ContatoQuePediuParaPararNaConversaMaisNovaNaoRecebe()
    {
        var (conversa, falhou) = FalhaDeQuemEscreveuHa(TimeSpan.FromDays(8));
        var aberta = Conversa.Abrir(_empresaId, WaId, Agora.AddHours(-30), "Maria Souza");
        aberta.RegistrarEntrada(Agora.AddHours(-30));
        _conversas.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.WhatsApp, WaId, Arg.Any<CancellationToken>()).Returns(aberta);
        _conversas.ListarMensagensAsync(_empresaId, aberta.Id, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Mensagem.Entrada(_empresaId, aberta.Id, Agora.AddHours(-30), TipoConteudoMensagem.Texto, "PARAR")]);

        var r = await Reenviar(conversa, falhou);

        r.Status.Should().Be(StatusMensagem.Falhou);
        r.Erro.Should().Contain("parar");
        await _whatsapp.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public async Task CatalogoQueFalhaRegistraAFalhaSemDerrubarOReenvio()
    {
        _catalogo.ListarAsync(_empresaId, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ModeloAtendimento>>(_ => throw new CatalogoModelosException("Lista de mensagens padrão fora do ar."));
        var (conversa, falhou) = FalhaDeQuemEscreveuHa(TimeSpan.FromDays(2));

        var r = await Reenviar(conversa, falhou);

        r.Status.Should().Be(StatusMensagem.Falhou);
        _uow.CommitCount.Should().Be(1);
        await _whatsapp.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }
}
