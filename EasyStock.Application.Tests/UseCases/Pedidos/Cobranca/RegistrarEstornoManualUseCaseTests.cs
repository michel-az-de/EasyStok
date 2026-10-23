using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// #1686 (item 10): devolução em dinheiro não tem comprovante; exigir referência obrigava a inventar
/// um texto. Nos outros meios (Pix direto, cartão, transferência) a referência continua obrigatória.
/// </summary>
public class RegistrarEstornoManualUseCaseTests
{
    private static RegistrarEstornoManualInput Entrada(CobrancaPedidoFixture f, string metodo, string? referencia) =>
        new(f.EmpresaId, f.Pedido.Id, Guid.NewGuid(), f.Pedido.Pagamentos.Single().Id, 10m, metodo, "Desistência",
            referencia, true, Guid.NewGuid(), "Dona", NivelAcesso.Admin);

    private static (CobrancaPedidoFixture F, RegistrarEstornoManualUseCase Caso) Montar()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Cancelado);
        f.AdicionarPagamento(referencia: null);
        f.PedidoRepo.ListarEstornosManuaisAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>())
            .Returns(new List<PedidoEstornoManual>());
        f.Uow.SetupExecuteInTransactionSemRetry<PedidoEstornoManual>();
        return (f, new RegistrarEstornoManualUseCase(f.PedidoRepo, f.CobrancaRepo, f.CaixaRepo, f.Uow, f.Relogio));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Devolucao_em_dinheiro_registra_sem_referencia(string? referencia)
    {
        var (f, caso) = Montar();

        var estorno = await caso.ExecuteAsync(Entrada(f, "dinheiro", referencia));

        estorno.Referencia.Should().BeEmpty();
        estorno.Metodo.Should().Be("dinheiro");
        await f.PedidoRepo.Received(1).AdicionarEstornoManualAsync(Arg.Any<PedidoEstornoManual>());
        await f.Uow.Received(1).CommitAsync();
    }

    [Theory]
    [InlineData("pix")]
    [InlineData("transferencia")]
    [InlineData("credito")]
    public async Task Outros_meios_continuam_exigindo_referencia(string metodo)
    {
        var (f, caso) = Montar();

        await FluentActions.Invoking(() => caso.ExecuteAsync(Entrada(f, metodo, "  ")))
            .Should().ThrowAsync<UseCaseValidationException>().WithMessage("*comprovante ou referência*");
        await f.Uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Devolucao_em_dinheiro_repetida_sem_referencia_devolve_o_mesmo_registro()
    {
        var (f, caso) = Montar();
        var entrada = Entrada(f, "dinheiro", null);
        var primeiro = await caso.ExecuteAsync(entrada);
        f.PedidoRepo.ListarEstornosManuaisAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>())
            .Returns(new List<PedidoEstornoManual> { primeiro });

        var repetido = await caso.ExecuteAsync(entrada);

        repetido.Should().BeSameAs(primeiro);
    }
}
