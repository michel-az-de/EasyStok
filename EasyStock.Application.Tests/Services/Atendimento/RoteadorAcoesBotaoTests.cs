using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.AcoesBotao;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Atendimento;

public class RoteadorAcoesBotaoTests
{
    private static readonly DateTime Agora = new(2026, 9, 26, 15, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConversaRepository _conversaRepository = Substitute.For<IConversaRepository>();

    private Conversa NovaConversa()
    {
        var conversa = Conversa.Abrir(_empresaId, "5511999998888", Agora, "Maria");
        conversa.RegistrarEntrada(Agora);
        return conversa;
    }

    [Fact]
    public async Task ConfirmaEnderecoSemLlm()
    {
        // Prova estrutural: o roteador não depende de IAgenteLlmClient; a confirmação de um toque é
        // resolvida pelo handler. A prova de que o webhook não enfileira o turno do agente para
        // "acao:" está em ProcessarEventoWhatsAppUseCaseTests.BotaoAcaoNaoChamaAgente.
        var roteador = new RoteadorAcoesBotao(
            [new ConfirmarEnderecoAcaoBotao(new EscalarConversaUseCase(_conversaRepository, Substitute.For<INotificadorService>(), Substitute.For<IOperacaoEventPublisher>()), ConfirmacaoEnderecoFake.Nova(), _conversaRepository)],
            NullLogger<RoteadorAcoesBotao>.Instance);
        var conversa = NovaConversa();

        var tratado = await roteador.ExecutarAsync(_empresaId, conversa, "acao:confirmar_endereco:end-123", Agora);

        tratado.Should().BeTrue();
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        await _conversaRepository.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Sistema && m.ExternoId == null && m.Texto!.Contains("end-123")),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("acao:confirmar_endereco:a:b", "confirmar_endereco", "a:b")]
    [InlineData("acao:escolher_janela:", "escolher_janela", "")]
    [InlineData("acao:avaliacao", "avaliacao", "")]
    public void InterpretaIdDoBotao(string id, string nome, string payload)
    {
        RoteadorAcoesBotao.TryInterpretar(id, out var n, out var p).Should().BeTrue();
        n.Should().Be(nome);
        p.Should().Be(payload);
    }

    [Theory]
    [InlineData("confirmar_endereco:1")]
    [InlineData("acao:")]
    [InlineData(null)]
    public void RecusaIdForaDoPadrao(string? id) =>
        RoteadorAcoesBotao.TryInterpretar(id, out _, out _).Should().BeFalse();

    [Fact]
    public async Task AcaoDesconhecidaNaoTrata()
    {
        var roteador = new RoteadorAcoesBotao([], NullLogger<RoteadorAcoesBotao>.Instance);
        var conversa = NovaConversa();

        var tratado = await roteador.ExecutarAsync(_empresaId, conversa, "acao:inexistente:1", Agora);

        tratado.Should().BeFalse();
        conversa.Situacao.Should().Be(SituacaoConversa.Automatica);
    }
}
