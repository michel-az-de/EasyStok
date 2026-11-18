using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// #2843: a alteração da comanda leva o pedido Pronto de volta a Preparando e só devolve as linhas do cardápio. A linha
/// lançada à mão com produto continua baixada. Cancelar nesse estado tem de devolver o que está baixado, não decidir
/// pelo status.
/// </summary>
public class CancelarPedidoDevolveEstoqueTests
{
    [Fact]
    public async Task Cancelar_pedido_em_preparando_com_saida_aberta_devolve_o_estoque()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Preparando);
        var produtoId = Guid.NewGuid();
        var avulso = new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = f.Pedido.Id, ProdutoId = produtoId, Nome = "Molho extra",
            Quantidade = 2, PrecoUnitario = 8m
        };
        avulso.RecalcularSubtotal();
        f.Pedido.Itens.Add(avulso);
        var refDoc = $"{f.Pedido.Id}:{avulso.Id}";
        var lote = new ItemEstoque { Id = Guid.NewGuid(), EmpresaId = f.EmpresaId, ProdutoId = produtoId, QuantidadeAtual = Quantidade.From(3) };
        var itemRepo = Substitute.For<IItemEstoqueRepository>();
        var movRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
        movRepo.ExisteReferenciaAsync(f.EmpresaId, produtoId, refDoc, NaturezaMovimentacaoEstoque.Venda, Arg.Any<CancellationToken>()).Returns(true);
        movRepo.GetPorReferenciaAsync(f.EmpresaId, produtoId, refDoc, NaturezaMovimentacaoEstoque.Venda, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(), EmpresaId = f.EmpresaId, ProdutoId = produtoId, ItemEstoqueId = lote.Id, ItemEstoque = lote,
                Tipo = TipoMovimentacaoEstoque.Saida, Natureza = NaturezaMovimentacaoEstoque.Venda,
                Quantidade = Quantidade.From(2), DocumentoReferencia = refDoc
            }
        });
        itemRepo.GetByIdComLockAsync(f.EmpresaId, lote.Id).Returns(lote);

        await Cancelar(f, itemRepo, movRepo).ExecuteAsync(new CancelarPedidoCommand(f.EmpresaId, f.Pedido.Id, Motivo: "Cliente desistiu"));

        lote.QuantidadeAtual!.Value.Should().Be(5, "a saída aberta da linha avulsa volta ao lote");
        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(m => m.Natureza == NaturezaMovimentacaoEstoque.Estorno));
    }

    [Fact]
    public async Task Cancelar_pedido_em_preparando_sem_saida_nao_mexe_no_estoque()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Preparando);
        var itemRepo = Substitute.For<IItemEstoqueRepository>();
        var movRepo = Substitute.For<IMovimentacaoEstoqueRepository>();

        await Cancelar(f, itemRepo, movRepo).ExecuteAsync(new CancelarPedidoCommand(f.EmpresaId, f.Pedido.Id));

        await movRepo.DidNotReceiveWithAnyArgs().InsertAsync(default!);
        await itemRepo.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    private static CancelarPedidoUseCase Cancelar(CobrancaPedidoFixture f, IItemEstoqueRepository itemRepo, IMovimentacaoEstoqueRepository movRepo)
    {
        var estoque = new PedidoEstoqueIntegrationService(itemRepo, movRepo, f.Publicador, Options.Create(new PedidoEstoqueOptions()),
            NullLogger<PedidoEstoqueIntegrationService>.Instance);
        return new CancelarPedidoUseCase(f.PedidoRepo, estoque,
            new EfeitosCancelamentoPedido(Substitute.For<IContaReceberRepository>(), f.VagaRepo, NullLogger<EfeitosCancelamentoPedido>.Instance),
            f.Uow, NullLogger<CancelarPedidoUseCase>.Instance, f.CobrancaRepo, f.Publicador, f.OperacaoEventos);
    }
}
