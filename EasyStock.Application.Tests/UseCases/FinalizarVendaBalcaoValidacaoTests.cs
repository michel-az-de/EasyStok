using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.FinalizarVendaBalcao;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases;

public class FinalizarVendaBalcaoValidacaoTests
{
    // A validação roda antes de qualquer use case composto ser tocado, então eles ficam nulos no teste.
    private static FinalizarVendaBalcaoUseCase Build()
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<FinalizarVendaBalcaoResult>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<FinalizarVendaBalcaoResult>>>()(ci.Arg<CancellationToken>()));
        return new FinalizarVendaBalcaoUseCase(null!, null!, null!, null!, null!, null!, uow,
            NullLogger<FinalizarVendaBalcaoUseCase>.Instance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task ProdutoExistente_ComQuantidadeNaoPositiva_Recusa(decimal quantidade)
    {
        // #2401: a checagem de quantidade só valia para produto novo; o existente passava com 0 ou negativo.
        var cmd = new FinalizarVendaBalcaoCommand(Guid.NewGuid(),
            Itens: [new FinalizarVendaBalcaoItemInput("Bolo", quantidade, 10m, ProdutoId: Guid.NewGuid())]);

        var act = () => Build().ExecuteAsync(cmd);

        (await act.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Contain("quantidade");
    }
}
