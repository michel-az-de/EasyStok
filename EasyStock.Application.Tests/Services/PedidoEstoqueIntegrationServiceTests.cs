using EasyStock.Application.Events.Estoque;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.Services;

public class PedidoEstoqueIntegrationServiceTests
{
    private static (PedidoEstoqueIntegrationService svc,
        IItemEstoqueRepository itemRepo,
        IMovimentacaoEstoqueRepository movRepo) Build(
        bool permiteNegativo = false, bool requerEstoque = false)
    {
        var (svc, itemRepo, movRepo, _) = BuildComPublicador(permiteNegativo, requerEstoque);
        return (svc, itemRepo, movRepo);
    }

    private static (PedidoEstoqueIntegrationService svc,
        IItemEstoqueRepository itemRepo,
        IMovimentacaoEstoqueRepository movRepo,
        IPublicadorEventoIntegracao publicador) BuildComPublicador(
        bool permiteNegativo = true, bool requerEstoque = false)
    {
        var itemRepo = Substitute.For<IItemEstoqueRepository>();
        var movRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
        var publicador = Substitute.For<IPublicadorEventoIntegracao>();
        var opts = Options.Create(new PedidoEstoqueOptions
        {
            PermiteEstoqueNegativo = permiteNegativo,
            RequerEstoqueExistente = requerEstoque
        });
        var svc = new PedidoEstoqueIntegrationService(itemRepo, movRepo, publicador, opts, NullLogger<PedidoEstoqueIntegrationService>.Instance);
        return (svc, itemRepo, movRepo, publicador);
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
    public async Task DescontarAsync_ignora_lote_vazio_e_baixa_o_lote_com_saldo()
    {
        var (svc, itemRepo, movRepo) = Build(permiteNegativo: true);
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaId, produtoId, qty: 5);

        ItemEstoque Lote(decimal qtd, int dias) => new()
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, LojaId = lojaId, ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(qtd), Status = StatusItemEstoque.Ok,
            ValidadeEm = Validade.From(DateTime.UtcNow.AddDays(dias))
        };
        var vazio = Lote(0, -10);   // mais antigo: o FEFO antigo o escolhia sempre
        var cheio = Lote(50, 30);
        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[] { vazio, cheio });

        await svc.DescontarAsync(pedido);

        cheio.QuantidadeAtual.Value.Should().Be(45);
        cheio.QuantidadeDescoberta.Value.Should().Be(0);
        vazio.QuantidadeDescoberta.Value.Should().Be(0);
        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(m => m.ItemEstoqueId == cheio.Id));
    }

    [Fact]
    public async Task DescontarAsync_recusa_falta_de_saldo_apos_lock_quando_descoberto_desabilitado()
    {
        var (svc, itemRepo, movRepo) = Build(permiteNegativo: false);
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var loteId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaId, produtoId, qty: 5);
        ItemEstoque Lote(decimal saldo) => new()
        {
            Id = loteId, EmpresaId = empresaId, LojaId = lojaId, ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(saldo)
        };
        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[] { Lote(5) });
        // Outro pedido consumiu o lote antes de esta transação adquirir o lock.
        var atual = Lote(2);
        itemRepo.GetByIdComLockAsync(empresaId, loteId).Returns(atual);

        await Assert.ThrowsAsync<EstoqueInsuficienteException>(() => svc.DescontarAsync(pedido));

        atual.QuantidadeDescoberta.Value.Should().Be(0);
        await movRepo.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
    }

    [Fact]
    public async Task DescontarAsync_distribui_quantidade_fracionaria_entre_lotes_em_fefo()
    {
        var (svc, itemRepo, movRepo) = Build();
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaId, produtoId, qty: 2.5m);
        ItemEstoque Lote(decimal saldo, int dias) => new()
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, LojaId = lojaId, ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(saldo), ValidadeEm = Validade.From(DateTime.UtcNow.AddDays(dias))
        };
        var primeiro = Lote(1.5m, 1);
        var segundo = Lote(3m, 2);
        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[] { segundo, primeiro });

        await svc.DescontarAsync(pedido);

        primeiro.QuantidadeAtual.Value.Should().Be(0);
        segundo.QuantidadeAtual.Value.Should().Be(2m);
        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(m => m.ItemEstoqueId == primeiro.Id && m.Quantidade.Value == 1.5m));
        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(m => m.ItemEstoqueId == segundo.Id && m.Quantidade.Value == 1m));
    }

    [Fact]
    public async Task DevolverItemAsync_restaura_os_lotes_originais_e_abate_descoberto()
    {
        var (svc, itemRepo, movRepo) = Build();
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaId, produtoId, qty: 5);
        var item = pedido.Itens.Single();
        var referencia = $"{pedido.Id}:{item.Id}";
        ItemEstoque Lote(decimal descoberto) => new()
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, LojaId = lojaId, ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(0), QuantidadeDescoberta = Quantidade.From(descoberto)
        };
        var primeiro = Lote(0);
        var segundo = Lote(1);
        MovimentacaoEstoque Saida(ItemEstoque lote, decimal quantidade) => new()
        {
            EmpresaId = empresaId, ProdutoId = produtoId, ItemEstoqueId = lote.Id,
            DocumentoReferencia = referencia, Tipo = TipoMovimentacaoEstoque.Saida,
            Natureza = NaturezaMovimentacaoEstoque.Venda, Quantidade = Quantidade.From(quantidade)
        };
        movRepo.ExisteReferenciaAsync(empresaId, produtoId, referencia, NaturezaMovimentacaoEstoque.Venda, Arg.Any<CancellationToken>()).Returns(true);
        movRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[] { Saida(primeiro, 2), Saida(segundo, 3) });
        itemRepo.GetByIdComLockAsync(empresaId, primeiro.Id).Returns(primeiro);
        itemRepo.GetByIdComLockAsync(empresaId, segundo.Id).Returns(segundo);

        await svc.DevolverItemAsync(pedido, item);

        primeiro.QuantidadeAtual.Value.Should().Be(2);
        segundo.QuantidadeAtual.Value.Should().Be(2);
        segundo.QuantidadeDescoberta.Value.Should().Be(0);
        await movRepo.Received(2).InsertAsync(Arg.Is<MovimentacaoEstoque>(m => m.Natureza == NaturezaMovimentacaoEstoque.Estorno));
    }

    [Fact]
    public async Task DescontarAsync_nao_baixa_lote_bloqueado()
    {
        var (svc, itemRepo, _) = Build(permiteNegativo: false);
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaId, produtoId, qty: 2);
        var bloqueado = new ItemEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, LojaId = lojaId, ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(10), Status = StatusItemEstoque.Bloqueado
        };
        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[] { bloqueado });

        // Sem lote operavel e sem requerer estoque: ignora o desconto, nao toca no bloqueado.
        await svc.DescontarAsync(pedido);

        bloqueado.QuantidadeAtual.Value.Should().Be(10);
    }

    [Fact]
    public void PermiteEstoqueNegativo_default_true()
    {
        new PedidoEstoqueOptions().PermiteEstoqueNegativo.Should().BeTrue();
    }

    [Fact] // S17 / RN-48: falta de saldo vira descoberto auditável, não bloqueio.
    public async Task SemSaldoRegistraDescobertoENaoLanca()
    {
        var (svc, itemRepo, movRepo, _) = BuildComPublicador(permiteNegativo: true);
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
        alvo.QuantidadeDescoberta.Value.Should().Be(3);
        var item = pedido.Itens.Single();
        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(m =>
            m.Quantidade.Value == 5
            && m.DocumentoReferencia == $"{pedido.Id}:{item.Id}"
            && m.Descricao == $"pedido {pedido.Id}: 3 un a descoberto"));
    }

    [Fact] // S17: o desacerto vira evento para S18 (SSE estoque.desacerto) e S22 (ajuste).
    public async Task SemSaldoPublicaEstoqueDesacertadoEvent()
    {
        var (svc, itemRepo, _, publicador) = BuildComPublicador(permiteNegativo: true);
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaId, produtoId, qty: 5);
        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[]
        {
            new ItemEstoque
            {
                Id = Guid.NewGuid(), EmpresaId = empresaId, LojaId = lojaId,
                ProdutoId = produtoId, QuantidadeAtual = Quantidade.From(2)
            }
        });

        await svc.DescontarAsync(pedido);

        await publicador.Received(1).PublicarAsync(
            empresaId,
            EstoqueDesacertadoEvent.TipoEventoOutbox,
            "Pedido",
            pedido.Id,
            Arg.Is<EstoqueDesacertadoEvent>(e =>
                e.ProdutoId == produtoId && e.PedidoId == pedido.Id && e.Falta == 3m),
            Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ComSaldoNaoPublicaEstoqueDesacertadoEvent()
    {
        var (svc, itemRepo, _, publicador) = BuildComPublicador(permiteNegativo: true);
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaId, produtoId, qty: 2);
        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[]
        {
            new ItemEstoque
            {
                Id = Guid.NewGuid(), EmpresaId = empresaId, LojaId = lojaId,
                ProdutoId = produtoId, QuantidadeAtual = Quantidade.From(5)
            }
        });

        await svc.DescontarAsync(pedido);

        await publicador.DidNotReceiveWithAnyArgs().PublicarAsync<EstoqueDesacertadoEvent>(
            default, default!, default!, default, default!);
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

    [Fact] // #939: estorno por item quando houve saída (Venda) anterior por este pedido+item.
    public async Task DevolverItemAsync_estorna_quando_houve_venda_anterior()
    {
        var (svc, itemRepo, movRepo) = Build();
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaId, produtoId, qty: 3);
        var item = pedido.Itens.Single();

        movRepo.ExisteReferenciaAsync(empresaId, produtoId, $"{pedido.Id}:{item.Id}",
            NaturezaMovimentacaoEstoque.Venda, Arg.Any<CancellationToken>()).Returns(true);
        var alvo = new ItemEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, LojaId = lojaId,
            ProdutoId = produtoId, QuantidadeAtual = Quantidade.From(0)
        };
        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[] { alvo });

        await svc.DevolverItemAsync(pedido, item);

        alvo.QuantidadeAtual!.Value.Should().Be(3);
        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(
            m => m.Natureza == NaturezaMovimentacaoEstoque.Estorno));
    }

    [Fact] // #939: sem Venda anterior, não há o que estornar (idempotência).
    public async Task DevolverItemAsync_pula_quando_sem_venda_anterior()
    {
        var (svc, _, movRepo) = Build();
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = PedidoComItem(empresaId, lojaId, produtoId, qty: 3);

        await svc.DevolverItemAsync(pedido, pedido.Itens.Single());

        await movRepo.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
    }
}
