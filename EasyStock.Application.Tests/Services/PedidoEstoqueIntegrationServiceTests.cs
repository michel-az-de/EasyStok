using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Application.Tests.Services;

public class PedidoEstoqueIntegrationServiceTests
{
    private static (PedidoEstoqueIntegrationService svc,
        IItemEstoqueRepository itemRepo,
        IMovimentacaoEstoqueRepository movRepo) Build(
        bool permiteNegativo = false, bool requerEstoque = false)
    {
        var itemRepo = Substitute.For<IItemEstoqueRepository>();
        var movRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
        var opts = Options.Create(new PedidoEstoqueOptions
        {
            PermiteEstoqueNegativo = permiteNegativo,
            RequerEstoqueExistente = requerEstoque
        });
        var svc = new PedidoEstoqueIntegrationService(itemRepo, movRepo, opts, NullLogger<PedidoEstoqueIntegrationService>.Instance);
        return (svc, itemRepo, movRepo);
    }

    private static Pedido PedidoComItem(Guid empresaId, Guid lojaId, Guid produtoId, decimal qty)
    {
        var p = Pedido.Criar(empresaId, cliente: null, lojaId, "web");
        p.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(),
            PedidoId = p.Id,
            ProdutoId = produtoId,
            Nome = "X",
            Quantidade = qty,
            PrecoUnitario = 10m
        });
        return p;
    }

    [Fact]
    public async Task DescontarAsync_lanca_quando_estoque_insuficiente_e_PermiteNegativo_false()
    {
        var (svc, itemRepo, _) = Build(permiteNegativo: false);
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaId, produtoId, qty: 5);

        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[]
        {
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                LojaId = lojaId,
                ProdutoId = produtoId,
                QuantidadeAtual = Quantidade.From(2)
            }
        });

        await Assert.ThrowsAsync<EstoqueInsuficienteException>(() => svc.DescontarAsync(pedido));
    }

    [Fact]
    public async Task DescontarAsync_clampa_quando_PermiteNegativo_true()
    {
        var (svc, itemRepo, movRepo) = Build(permiteNegativo: true);
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaId, produtoId, qty: 5);

        var alvo = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            LojaId = lojaId,
            ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(2)
        };
        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[] { alvo });

        await svc.DescontarAsync(pedido);

        alvo.QuantidadeAtual!.Value.Should().Be(0);
        await movRepo.Received(1).InsertAsync(Arg.Any<MovimentacaoEstoque>());
    }

    [Fact]
    public async Task DescontarAsync_idempotente_pula_se_movimentacao_ja_existe()
    {
        var (svc, itemRepo, movRepo) = Build();
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaId, produtoId, qty: 1);

        // Idempotência por (pedidoId:itemId), não só pedidoId.
        var pedidoItemId = pedido.Itens.Single().Id;
        movRepo.ExisteReferenciaAsync(empresaId, produtoId, $"{pedido.Id}:{pedidoItemId}",
                NaturezaMovimentacaoEstoque.Venda, Arg.Any<CancellationToken>())
            .Returns(true);

        await svc.DescontarAsync(pedido);

        await itemRepo.DidNotReceive().GetByProdutoAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
        await movRepo.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
    }

    [Fact]
    public async Task DescontarAsync_sem_estoque_lanca_quando_RequerEstoqueExistente_true()
    {
        var (svc, itemRepo, _) = Build(requerEstoque: true);
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaId, produtoId, qty: 1);

        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(Array.Empty<ItemEstoque>());

        await Assert.ThrowsAsync<EasyStock.Application.UseCases.Common.UseCaseValidationException>(
            () => svc.DescontarAsync(pedido));
    }

    [Fact]
    public async Task DescontarAsync_sem_estoque_passa_quando_RequerEstoqueExistente_false()
    {
        var (svc, itemRepo, movRepo) = Build(requerEstoque: false);
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaId, produtoId, qty: 1);

        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(Array.Empty<ItemEstoque>());

        await svc.DescontarAsync(pedido); // não lança

        await movRepo.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
    }

    [Fact]
    public async Task DescontarAsync_match_exato_por_loja()
    {
        var (svc, itemRepo, movRepo) = Build();
        var empresaId = Guid.NewGuid();
        var lojaA = Guid.NewGuid();
        var lojaB = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaA, produtoId, qty: 1);

        var itemB = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            LojaId = lojaB, // outra loja!
            ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(100)
        };
        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[] { itemB });

        await svc.DescontarAsync(pedido);

        // ItemB não pertence à loja do pedido — não deve ser descontado.
        itemB.QuantidadeAtual!.Value.Should().Be(100);
        await movRepo.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
    }
}
