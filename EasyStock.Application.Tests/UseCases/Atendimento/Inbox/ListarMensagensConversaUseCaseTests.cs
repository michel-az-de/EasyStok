using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Inbox;

public class ListarMensagensConversaUseCaseTests
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _conversa = Guid.NewGuid();
    private readonly IConversaRepository _repo = Substitute.For<IConversaRepository>();

    [Fact]
    public async Task ConfereQueAConversaExistePelaLeituraLeve()
    {
        // #1566: o histórico é relido a cada polling; carregar a conversa inteira (rastreada, com o contexto) só
        // para saber se ela existe era desperdício.
        _repo.ObterSituacaoAsync(_empresa, _conversa, Arg.Any<CancellationToken>()).Returns(SituacaoConversa.Assumida);
        _repo.ListarMensagensAsync(_empresa, _conversa, null, 20, Arg.Any<CancellationToken>()).Returns(new List<Mensagem>());

        var mensagens = await new ListarMensagensConversaUseCase(_repo)
            .ExecuteAsync(new ListarMensagensConversaQuery(_empresa, _conversa, null, 20));

        mensagens.Should().BeEmpty();
        await _repo.Received(1).ListarMensagensAsync(_empresa, _conversa, null, 20, Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().ObterPorIdAsync(default, default, default);
    }

    [Fact]
    public async Task ConversaQueNaoExisteNaEmpresaNaoListaNada()
    {
        _repo.ObterSituacaoAsync(_empresa, _conversa, Arg.Any<CancellationToken>()).Returns((SituacaoConversa?)null);

        var act = () => new ListarMensagensConversaUseCase(_repo)
            .ExecuteAsync(new ListarMensagensConversaQuery(_empresa, _conversa, null, 20));

        await act.Should().ThrowAsync<ConversaNaoEncontradaException>();
        await _repo.DidNotReceiveWithAnyArgs().ListarMensagensAsync(default, default, default, default, default);
    }
}
