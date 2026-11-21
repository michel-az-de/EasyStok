using EasyStock.Application.Tests.Helpers;
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
        var repositorio = Substitute.For<IConversaRepository>().EspelharLeituraComLock();
        repositorio.ObterPorIdAsync(empresa, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        var uow = Substitute.For<IUnitOfWork>().SetupExecuteInTransactionSemRetry<ConversaSituacaoResult>();
        var fila = Substitute.For<IQueueService>();
        var useCase = new GerenciarConversaAtendimentoUseCase(repositorio, uow, fila: fila);
        await useCase.LiberarAutomaticoAsync(new(empresa, Guid.NewGuid(), conversa.Id, "Atendente da sprint"));
        conversa.Situacao.Should().Be(SituacaoConversa.Automatica);
        await uow.Received(1).CommitAsync();
        await fila.Received(assumida ? 1 : 0).EnqueueAsync(FilaAtendimentoNomes.TurnoAgente,
            Arg.Is<ProcessarTurnoAgenteJob>(j => j.EmpresaId == empresa && j.ConversaId == conversa.Id));
    }

    [Fact]
    public async Task DevolverAoRoboTiraAPausaDaPessoa()
    {
        // #2249: a pessoa encaminhada à responsável volta ao robô só quando a equipe devolve a conversa.
        var empresa = Guid.NewGuid();
        var cliente = EasyStock.Domain.Entities.Cliente.Criar(empresa, "Joao Fornecedor");
        cliente.PausarAtendimentoAutomatico("orçamento de fornecedor", DateTime.UtcNow);
        var conversa = Conversa.Abrir(empresa, "5511912345678", DateTime.UtcNow);
        conversa.VincularCliente(cliente.Id);
        conversa.Assumir(DateTime.UtcNow);
        var repositorio = Substitute.For<IConversaRepository>().EspelharLeituraComLock();
        repositorio.ObterPorIdAsync(empresa, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        var clientes = Substitute.For<IClienteRepository>();
        clientes.GetByIdAsync(empresa, cliente.Id).Returns(cliente);
        var uow = Substitute.For<IUnitOfWork>().SetupExecuteInTransactionSemRetry<ConversaSituacaoResult>();

        await new GerenciarConversaAtendimentoUseCase(repositorio, uow, clientes: clientes)
            .LiberarAutomaticoAsync(new(empresa, Guid.NewGuid(), conversa.Id, "Atendente"));

        cliente.AtendimentoAutomaticoPausado.Should().BeFalse();
        cliente.MotivoPausaAtendimento.Should().BeNull();
    }
}
