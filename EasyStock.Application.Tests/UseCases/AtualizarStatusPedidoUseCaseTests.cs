using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Application.UseCases.Financeiro.Integracao;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class AtualizarStatusPedidoUseCaseTests
{
    private static (AtualizarStatusPedidoUseCase uc, IPedidoRepository repo, IItemEstoqueRepository itemRepo, IMovimentacaoEstoqueRepository movRepo, IUnitOfWork uow) Build(bool permiteNegativo = true)
    {
        var pedidoRepo = Substitute.For<IPedidoRepository>();
        var itemRepo = Substitute.For<IItemEstoqueRepository>();
        var movRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var configRepo = Substitute.For<IConfiguracaoLojaRepository>();
        var contaReceberRepo = Substitute.For<IContaReceberRepository>();
        var categoriaRepo = Substitute.For<ICategoriaFinanceiraRepository>();
        var criarContaReceber = new CriarContaReceberUseCase(contaReceberRepo, categoriaRepo,
            Substitute.For<ICentroCustoRepository>(), uow, NullLogger<CriarContaReceberUseCase>.Instance);
        var gerarCr = new GerarContaReceberDePedidoUseCase(contaReceberRepo, categoriaRepo, configRepo,
            criarContaReceber, NullLogger<GerarContaReceberDePedidoUseCase>.Instance);
        var opts = Options.Create(new PedidoEstoqueOptions { PermiteEstoqueNegativo = permiteNegativo });
        var integ = new PedidoEstoqueIntegrationService(itemRepo, movRepo, opts, NullLogger<PedidoEstoqueIntegrationService>.Instance);
        var uc = new AtualizarStatusPedidoUseCase(pedidoRepo, integ, configRepo, gerarCr, uow, NullLogger<AtualizarStatusPedidoUseCase>.Instance);
        return (uc, pedidoRepo, itemRepo, movRepo, uow);
    }

    private static Pedido NovoPedido(Guid empresaId, Guid lojaId, Guid produtoId, decimal qty, string status = "aguardando")
    {
        var p = Pedido.Criar(empresaId, null, lojaId, "web");
        p.Status = status;
        p.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(),
            PedidoId = p.Id,
            ProdutoId = produtoId,
            Nome = "Item",
            Quantidade = qty,
            PrecoUnitario = 10m
        });
        return p;
    }

    [Fact]
    public async Task Status_aguardando_para_preparando_nao_mexe_estoque()
    {
        var (uc, repo, itemRepo, movRepo, _) = Build();
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, lojaId, produtoId, 2);
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        var cmd = new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "preparando");
        await uc.ExecuteAsync(cmd);

        await movRepo.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
        pedido.Status.Should().Be("preparando");
    }

    [Fact]
    public async Task Status_para_pronto_desconta_estoque_e_atualiza_status()
    {
        var (uc, repo, itemRepo, movRepo, _) = Build();
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, lojaId, produtoId, 2, "preparando");
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);
        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[]
        {
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                LojaId = lojaId,
                ProdutoId = produtoId,
                QuantidadeAtual = Quantidade.From(10)
            }
        });

        var cmd = new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "pronto");
        await uc.ExecuteAsync(cmd);

        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(
            m => m.Natureza == NaturezaMovimentacaoEstoque.Venda));
        pedido.Status.Should().Be("pronto");
    }

    [Fact]
    public async Task Transicao_invalida_lanca_e_status_nao_muda()
    {
        var (uc, repo, _, _, _) = Build();
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, Guid.NewGuid(), Guid.NewGuid(), 1, "entregue");
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        var cmd = new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "preparando");

        await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(cmd));
        pedido.Status.Should().Be("entregue");
    }

    [Fact]
    public async Task Cancelar_pedido_entregue_devolve_estoque()
    {
        var (uc, repo, itemRepo, movRepo, _) = Build();
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, lojaId, produtoId, 2, "entregue");
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        var item = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            LojaId = lojaId,
            ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(8)
        };
        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[] { item });
        // Simula que houve venda anterior pra devolução ser aplicável
        var itemId = pedido.Itens.Single().Id;
        movRepo.ExisteReferenciaAsync(empresaId, produtoId, $"{pedido.Id}:{itemId}",
                NaturezaMovimentacaoEstoque.Venda, Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "cancelado");
        await uc.ExecuteAsync(cmd);

        item.QuantidadeAtual!.Value.Should().Be(10); // devolvidos 2 unidades
        pedido.Status.Should().Be("cancelado");
    }
}
