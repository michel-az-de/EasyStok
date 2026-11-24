using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.Services;

public class R4IntegracaoFinalProvas
{
    [Fact]
    public async Task Baixa_e_devolucao_preservam_o_rateio_original_e_repetir_nao_duplica()
    {
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId, cliente: null, lojaId, "web");
        var item = new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, ProdutoId = produtoId,
            Nome = "Produto da prova de rateio", Quantidade = 0.9999m, PrecoUnitario = 10m
        };
        pedido.Itens.Add(item);
        var referencia = $"{pedido.Id}:{item.Id}";
        var agora = DateTime.UtcNow;
        var lotes = Enumerable.Range(1, 3).Select(dias => new ItemEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, LojaId = lojaId, ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(0.3333m), Status = StatusItemEstoque.Ok,
            ValidadeEm = Validade.From(agora.AddDays(dias))
        }).ToArray();
        var itemRepo = Substitute.For<IItemEstoqueRepository>();
        var movRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
        var movimentos = new List<MovimentacaoEstoque>();
        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(lotes);
        foreach (var lote in lotes)
            itemRepo.GetByIdComLockAsync(empresaId, lote.Id).Returns(lote);
        movRepo.When(repo => repo.InsertAsync(Arg.Any<MovimentacaoEstoque>()))
            .Do(chamada => movimentos.Add(chamada.Arg<MovimentacaoEstoque>()));
        movRepo.ExisteReferenciaAsync(empresaId, produtoId, referencia,
                Arg.Any<NaturezaMovimentacaoEstoque>(), Arg.Any<CancellationToken>())
            .Returns(chamada => movimentos.Any(m => m.EmpresaId == empresaId
                && m.ProdutoId == produtoId && m.DocumentoReferencia == referencia
                && m.Natureza == chamada.Arg<NaturezaMovimentacaoEstoque>()));
        movRepo.GetPorReferenciaAsync(empresaId, produtoId, referencia,
                NaturezaMovimentacaoEstoque.Venda, Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<MovimentacaoEstoque>)movimentos
                .Where(m => m.EmpresaId == empresaId && m.ProdutoId == produtoId
                    && m.DocumentoReferencia == referencia && m.Natureza == NaturezaMovimentacaoEstoque.Venda)
                .ToArray());
        var servico = new PedidoEstoqueIntegrationService(itemRepo, movRepo,
            Substitute.For<IPublicadorEventoIntegracao>(),
            Options.Create(new PedidoEstoqueOptions { PermiteEstoqueNegativo = false, RequerEstoqueExistente = true }),
            NullLogger<PedidoEstoqueIntegrationService>.Instance);

        await servico.DescontarAsync(pedido);

        var saidas = movimentos.ToArray();
        saidas.Should().HaveCount(3);
        saidas.Should().OnlyContain(m => m.Id != Guid.Empty && m.Tipo == TipoMovimentacaoEstoque.Saida
            && m.Natureza == NaturezaMovimentacaoEstoque.Venda && m.EstornadaEm == null);
        saidas.Select(m => m.Id).Should().OnlyHaveUniqueItems();
        saidas.Select(m => m.ValorTotal!.Valor).Should().Equal(3.33m, 3.33m, 3.34m);
        saidas.Sum(m => m.ValorTotal!.Valor).Should().Be(10m);
        saidas.Select(m => m.Quantidade.Value).Should().Equal(0.3333m, 0.3333m, 0.3333m);
        lotes.Should().OnlyContain(l => l.QuantidadeAtual.Value == 0m);

        await servico.DevolverAsync(pedido);

        var estornos = movimentos.Where(m => m.Natureza == NaturezaMovimentacaoEstoque.Estorno).ToArray();
        estornos.Should().HaveCount(3);
        estornos.Select(m => m.ValorTotal!.Valor).Should().Equal(3.33m, 3.33m, 3.34m);
        estornos.Sum(m => m.ValorTotal!.Valor).Should().Be(10m);
        foreach (var saida in saidas)
        {
            saida.EstornadaEm.Should().NotBeNull();
            var estorno = estornos.Single(m => m.MovimentacaoEstornadaId == saida.Id);
            estorno.Id.Should().NotBe(Guid.Empty);
            estorno.Tipo.Should().Be(TipoMovimentacaoEstoque.Entrada);
            estorno.ItemEstoqueId.Should().Be(saida.ItemEstoqueId);
            estorno.Quantidade.Should().Be(saida.Quantidade);
            estorno.ValorUnitario.Should().Be(saida.ValorUnitario);
            estorno.ValorTotal.Should().Be(saida.ValorTotal);
            estorno.DocumentoReferencia.Should().Be(referencia);
        }
        lotes.Should().OnlyContain(l => l.QuantidadeAtual.Value == 0.3333m
            && l.QuantidadeDescoberta.Value == 0m);
        var idsAntesDaRepeticao = movimentos.Select(m => m.Id).ToArray();

        await servico.DevolverAsync(pedido);

        movimentos.Select(m => m.Id).Should().Equal(idsAntesDaRepeticao);
        movimentos.Should().HaveCount(6);
        lotes.Should().OnlyContain(l => l.QuantidadeAtual.Value == 0.3333m
            && l.QuantidadeDescoberta.Value == 0m);
        await movRepo.Received(3).InsertAsync(Arg.Is<MovimentacaoEstoque>(m =>
            m.Natureza == NaturezaMovimentacaoEstoque.Estorno));
    }
}
