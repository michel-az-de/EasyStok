using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Atendimento.Reenvio;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Inbox;

/// <summary>#2833: reagir e apagar para todos saem pelo canal primeiro; só o que o canal confirmou é gravado.</summary>
public class AcoesNaMensagemUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc);
    private const string WaId = "5511982254398";

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _usuarioId = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly ICanalMensageria _whatsapp = Substitute.For<ICanalMensageria, ICanalComAcoesNaMensagem>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));
    private readonly Conversa _conversa;
    private readonly Mensagem _daLoja;
    private readonly Mensagem _doCliente;

    private ICanalComAcoesNaMensagem Acoes => (ICanalComAcoesNaMensagem)_whatsapp;

    public AcoesNaMensagemUseCaseTests()
    {
        _whatsapp.Canal.Returns(CanalConversa.WhatsApp);
        _conversa = Conversa.Abrir(_empresaId, WaId, Agora.AddHours(-3), "Maria");
        _conversa.RegistrarEntrada(Agora.AddHours(-3));
        _daLoja = Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Dona, Agora.AddHours(-2), TipoConteudoMensagem.Texto, "Mando o Pix?");
        _daLoja.ConfirmarEnvio("wpp:s:true_5511982254398@c.us_A");
        _doCliente = Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddHours(-3), TipoConteudoMensagem.Texto, "Quero 2", "wpp:s:false_5511982254398@c.us_B");
        _conversas.ObterPorIdAsync(_empresaId, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_conversa);
        _conversas.ObterMensagemComLockAsync(_empresaId, _conversa.Id, _daLoja.Id, Arg.Any<CancellationToken>()).Returns(_daLoja);
        _conversas.ObterMensagemComLockAsync(_empresaId, _conversa.Id, _doCliente.Id, Arg.Any<CancellationToken>()).Returns(_doCliente);
    }

    private AcoesNaMensagemUseCase UseCase(params ICanalMensageria[] canais) =>
        new(_conversas, new ResolvedorCanal(canais.Length == 0 ? [_whatsapp] : canais), _uow, _relogio);

    private ReagirMensagemCommand Reagir(Mensagem m, string? emoji) => new(_empresaId, _usuarioId, _conversa.Id, m.Id, emoji);
    private ApagarMensagemCommand Apagar(Mensagem m) => new(_empresaId, _usuarioId, _conversa.Id, m.Id);

    [Fact]
    public async Task Reacao_sai_pelo_canal_e_fica_gravada()
    {
        var r = await UseCase().ReagirAsync(Reagir(_doCliente, "👍"));

        await Acoes.Received(1).ReagirAsync(WaId, "wpp:s:false_5511982254398@c.us_B", "👍", Arg.Any<CancellationToken>());
        r.Reacao.Should().Be("👍");
        _doCliente.Reacao.Should().Be("👍");
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Reacao_que_o_canal_recusa_nao_e_gravada()
    {
        Acoes.ReagirAsync(default!, default!, default, default)
            .ReturnsForAnyArgs(Task.FromException(new WhatsAppCloudException(131000, "fora", ehPermanente: false)));

        var act = () => UseCase().ReagirAsync(Reagir(_doCliente, "👍"));

        await act.Should().ThrowAsync<FalhaEnvioCanalException>();
        _doCliente.Reacao.Should().BeNull();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Reacao_fora_da_janela_vira_o_erro_de_janela()
    {
        Acoes.ReagirAsync(default!, default!, default, default).ReturnsForAnyArgs(
            Task.FromException(new WhatsAppCloudException(WhatsAppCloudException.CodigoForaDaJanela, "131047", ehPermanente: true)));

        var act = () => UseCase().ReagirAsync(Reagir(_doCliente, "👍"));

        await act.Should().ThrowAsync<ForaDaJanelaAtendimentoException>();
    }

    [Fact]
    public async Task Apagar_sai_pelo_canal_e_grava_quem_apagou()
    {
        var r = await UseCase().ApagarParaTodosAsync(Apagar(_daLoja));

        await Acoes.Received(1).ApagarParaTodosAsync(WaId, "wpp:s:true_5511982254398@c.us_A", Arg.Any<CancellationToken>());
        r.ApagadaEm.Should().Be(Agora);
        _daLoja.ApagadaPorUsuarioId.Should().Be(_usuarioId);
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Apagar_mensagem_do_cliente_e_recusado_sem_chamar_o_canal()
    {
        var act = () => UseCase().ApagarParaTodosAsync(Apagar(_doCliente));

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
        await Acoes.DidNotReceiveWithAnyArgs().ApagarParaTodosAsync(default!, default!, default);
    }

    [Fact]
    public async Task Apagar_depois_de_48_horas_e_recusado_sem_chamar_o_canal()
    {
        _relogio.SetUtcNow(new DateTimeOffset(Agora.AddHours(47)));

        var act = () => UseCase().ApagarParaTodosAsync(Apagar(_daLoja));

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*48 h*");
        await Acoes.DidNotReceiveWithAnyArgs().ApagarParaTodosAsync(default!, default!, default);
        _daLoja.ApagadaEm.Should().BeNull();
    }

    [Fact]
    public async Task Canal_que_nao_apaga_responde_nao_suportado_e_nada_muda()
    {
        Acoes.ApagarParaTodosAsync(default!, default!, default)
            .ReturnsForAnyArgs(Task.FromException(new AcaoNaoSuportadaWhatsAppException("A Meta não apaga.")));

        var act = () => UseCase().ApagarParaTodosAsync(Apagar(_daLoja));

        await act.Should().ThrowAsync<AcaoNaoSuportadaWhatsAppException>();
        _daLoja.ApagadaEm.Should().BeNull();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Canal_sem_a_porta_de_acoes_responde_nao_suportado()
    {
        var semAcoes = Substitute.For<ICanalMensageria>();
        semAcoes.Canal.Returns(CanalConversa.WhatsApp);

        var act = () => UseCase(semAcoes).ReagirAsync(Reagir(_doCliente, "👍"));

        await act.Should().ThrowAsync<AcaoNaoSuportadaWhatsAppException>();
    }

    [Fact]
    public async Task Mensagem_de_outra_conversa_ou_empresa_e_404()
    {
        var act = () => UseCase().ApagarParaTodosAsync(new ApagarMensagemCommand(_empresaId, _usuarioId, _conversa.Id, Guid.NewGuid()));

        await act.Should().ThrowAsync<MensagemNaoEncontradaException>();
    }
}
