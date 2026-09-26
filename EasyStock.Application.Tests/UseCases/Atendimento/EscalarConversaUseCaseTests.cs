using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

public class EscalarConversaUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 9, 26, 15, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConversaRepository _conversaRepository = Substitute.For<IConversaRepository>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IOperacaoEventPublisher _eventPublisher = Substitute.For<IOperacaoEventPublisher>();
    private readonly EscalarConversaUseCase _useCase;

    public EscalarConversaUseCaseTests()
    {
        _useCase = new EscalarConversaUseCase(_conversaRepository, _notificador, _eventPublisher);
    }

    private Conversa NovaConversa()
    {
        var conversa = Conversa.Abrir(_empresaId, "5511999998888", Agora, "Maria");
        conversa.RegistrarEntrada(Agora);
        return conversa;
    }

    [Fact]
    public async Task PushParaTodasSubscriptions()
    {
        var conversa = NovaConversa();

        await _useCase.EscalarAsync(_empresaId, conversa, "cliente pediu desconto", Agora);

        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        await _conversaRepository.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Sistema && m.ExternoId == null
                                  && m.Texto!.Contains("cliente pediu desconto")),
            Arg.Any<CancellationToken>());

        // Evento no outbox (ADR-0030), sem usuário alvo: o Push vai para todas as subscriptions da empresa.
        await _notificador.Received(1).EnfileirarEventoAsync(
            TipoEventoNotificacao.ConversaEscalada,
            _empresaId,
            Arg.Is<string>(json => PayloadSemUsuarioComMotivo(json, conversa.Id)),
            conversa.Id,
            Arg.Any<CancellationToken>());

        await _eventPublisher.Received(1).PublicarAsync(
            "conversa.escalada", _empresaId, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConversaEncerradaNaoEscala()
    {
        var conversa = NovaConversa();
        conversa.Encerrar(Agora);

        await _useCase.EscalarAsync(_empresaId, conversa, "qualquer", Agora);

        await _conversaRepository.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
        await _notificador.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!, default, default);
        await _eventPublisher.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default, default!, default);
    }

    [Fact]
    public void ConversaEscaladaTemValor46() =>
        ((int)TipoEventoNotificacao.ConversaEscalada).Should().Be(46);

    private static bool PayloadSemUsuarioComMotivo(string json, Guid conversaId)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        return !raiz.TryGetProperty("usuarioId", out _)
               && raiz.GetProperty("conversaId").GetString() == conversaId.ToString()
               && raiz.GetProperty("cliente").GetString() == "Maria"
               && raiz.GetProperty("motivo").GetString() == "cliente pediu desconto";
    }
}
