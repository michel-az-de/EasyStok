using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

public class RetomarAtendimentoSprintTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetomarAutomatico_AgendaPendenciasSomenteNaTransicao(bool assumida)
    {
        var empresa = Guid.NewGuid();
        var conversa = Conversa.Abrir(empresa, "5511912345678", DateTime.UtcNow);
        if (assumida) conversa.Assumir(DateTime.UtcNow);
        var repositorio = Substitute.For<IConversaRepository>();
        repositorio.ObterPorIdAsync(empresa, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        var uow = Substitute.For<IUnitOfWork>();
        var fila = Substitute.For<IQueueService>();
        var useCase = new GerenciarConversaAtendimentoUseCase(repositorio, uow, fila: fila);
        await useCase.LiberarAutomaticoAsync(new(empresa, Guid.NewGuid(), conversa.Id, "Atendente da sprint"));
        conversa.Situacao.Should().Be(SituacaoConversa.Automatica);
        await uow.Received(1).CommitAsync();
        await fila.Received(assumida ? 1 : 0).EnqueueAsync(FilaAtendimentoNomes.TurnoAgente,
            Arg.Is<ProcessarTurnoAgenteJob>(j => j.EmpresaId == empresa && j.ConversaId == conversa.Id));
    }
}
