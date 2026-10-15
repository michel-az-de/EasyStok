using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>
/// N1: o commit que falha ao avaliar um evento deixa a entidade inválida rastreada. O serviço descarta o rastreamento
/// antes de gravar o desfecho do evento (senão o próximo commit reenvia o mesmo INSERT e falha também) e trata 23505 na
/// IdempotencyKey como "já enfileirado".
/// </summary>
public class NotificadorServiceAvaliacaoFalhaTests
{
    private readonly IEventoNotificacaoRepository _eventoRepository = Substitute.For<IEventoNotificacaoRepository>();
    private readonly IRotinaRepository _rotinaRepository = Substitute.For<IRotinaRepository>();
    private readonly IBloqueioNotificacaoRepository _bloqueioRepository = Substitute.For<IBloqueioNotificacaoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly NotificadorService _service;

    public NotificadorServiceAvaliacaoFalhaTests()
    {
        _service = new NotificadorService(
            _eventoRepository, _rotinaRepository, Substitute.For<ITemplateRepository>(), Substitute.For<IConsentimentoRepository>(),
            Substitute.For<IConfiguracaoCanalRepository>(), _bloqueioRepository, Substitute.For<IOutboxNotificacaoRepository>(),
            Substitute.For<IRendererTemplate>(), new ResolvedorCanal(), _unitOfWork, NullLogger<NotificadorService>.Instance);

        _bloqueioRepository.ListarAtivosAsync(Arg.Any<Guid?>(), Arg.Any<CanalNotificacao?>(), Arg.Any<CancellationToken>()).Returns([]);
        // Sem rotina o evento fecha como processado, sem outbox: o que importa aqui é o commit.
        _rotinaRepository.ListarAtivasAsync(Arg.Any<TipoEventoNotificacao?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private static EventoNotificacao NovoEvento() =>
        EventoNotificacao.Criar(TipoEventoNotificacao.ProdutoVencendo, Guid.NewGuid(), "{}");

    private void PrimeiroCommitFalha() =>
        _unitOfWork.CommitAsync().Returns(
            _ => Task.FromException<int>(new InvalidOperationException("commit inválido")),
            _ => Task.FromResult(1));

    [Fact]
    public async Task Commit_que_falha_descarta_o_rastreamento_antes_de_gravar_o_Falhado_com_o_motivo()
    {
        var evento = NovoEvento();
        PrimeiroCommitFalha();

        await _service.AvaliarEventoAsync(evento);

        evento.Status.Should().Be(StatusEventoNotificacao.Falhado);
        evento.ErroProcessamento.Should().Contain("commit inválido");
        // O primeiro Update é o da avaliação (evento Processado, que o commit inválido não gravou); o descarte vem
        // antes do segundo, o do desfecho Falhado.
        Received.InOrder(() =>
        {
            _eventoRepository.UpdateAsync(evento, Arg.Any<CancellationToken>());
            _unitOfWork.DescartarAlteracoesPendentes();
            _eventoRepository.UpdateAsync(evento, Arg.Any<CancellationToken>());
        });
        await _unitOfWork.Received(2).CommitAsync();
    }

    [Fact]
    public async Task Violacao_de_unicidade_no_commit_conta_como_ja_enfileirado_e_fecha_Processado()
    {
        var evento = NovoEvento();
        PrimeiroCommitFalha();
        _unitOfWork.EhViolacaoDeUnicidade(Arg.Any<Exception>()).Returns(true);

        await _service.AvaliarEventoAsync(evento);

        evento.Status.Should().Be(StatusEventoNotificacao.Processado);
        evento.ErroProcessamento.Should().BeNull();
        _unitOfWork.Received(1).DescartarAlteracoesPendentes();
    }

    [Fact]
    public async Task Se_nem_o_desfecho_grava_propaga_o_erro()
    {
        var evento = NovoEvento();
        _unitOfWork.CommitAsync().Returns(_ => Task.FromException<int>(new InvalidOperationException("banco fora")));

        var act = async () => await _service.AvaliarEventoAsync(evento);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
