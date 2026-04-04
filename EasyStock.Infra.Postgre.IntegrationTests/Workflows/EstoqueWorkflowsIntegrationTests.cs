using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Application.UseCases.ReporEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

public class EstoqueWorkflowsIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [Fact]
    public async Task RegistrarEntrada_deve_persistir_item_e_movimentacao()
    {
        if (!fixture.IsAvailable) return;
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        await using (var setupContext = fixture.CreateDbContext())
        {
            await SeedProdutoAsync(setupContext, empresaId, categoriaId, produtoId);
        }

        await using (var context = fixture.CreateDbContext())
        {
            var useCase = new RegistrarEntradaEstoqueUseCase(
                new ProdutoRepository(context),
                new ProdutoVariacaoRepository(context),
                new ItemEstoqueRepository(context),
                new MovimentacaoEstoqueRepository(context),
                context,
                NullLogger<RegistrarEntradaEstoqueUseCase>.Instance,
                new GeradorDescricaoFake("Descricao gerada por IA"));

            var result = await useCase.ExecuteAsync(new RegistrarEntradaEstoqueCommand(
                empresaId,
                produtoId,
                null,
                10,
                250m,
                399.90m,
                new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
                NaturezaMovimentacaoEstoque.Compra,
                "CAP3426",
                "LOTE-01",
                "ML-ABC",
                "Grafite",
                "Grafite",
                "Unico",
                "Fornecedor XPTO",
                null,
                "Primeira entrada",
                null,
                "DOC-01",
                new DimensoesInput(0.3m, 10.5m, 5.2m, 8.1m),
                "Mercado Livre"));

            result.DescricaoAnuncio.Should().Be("Descricao gerada por IA");
        }

        await using (var assertContext = fixture.CreateDbContext())
        {
            var item = await assertContext.ItensEstoque.SingleAsync();
            var movimentacao = await assertContext.MovimentacoesEstoque.SingleAsync();

            item.CodigoInterno.Should().Be("CAP3426");
            item.CodigoLote!.Value.Should().Be("LOTE-01");
            item.QuantidadeAtual.Value.Should().Be(10);
            item.CustoUnitario.Valor.Should().Be(250m);
            item.PrecoVendaSugerido!.Valor.Should().Be(399.90m);
            item.ChavePesquisa.Should().Contain("CAP3426");
            item.DescricaoAnuncio.Should().Be("Descricao gerada por IA");

            movimentacao.Tipo.Should().Be(TipoMovimentacaoEstoque.Entrada);
            movimentacao.Natureza.Should().Be(NaturezaMovimentacaoEstoque.Compra);
            movimentacao.Quantidade.Value.Should().Be(10);
            movimentacao.ValorTotal!.Valor.Should().Be(2500m);
        }
    }

    [Fact]
    public async Task ReporEstoque_deve_atualizar_quantidade_e_gerar_movimentacao()
    {
        if (!fixture.IsAvailable) return;
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await using (var setupContext = fixture.CreateDbContext())
        {
            await SeedProdutoAsync(setupContext, empresaId, categoriaId, produtoId);
            setupContext.ItensEstoque.Add(new ItemEstoque
            {
                Id = itemId,
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                QuantidadeInicial = Quantidade.From(5),
                QuantidadeAtual = Quantidade.From(5),
                CustoUnitario = Dinheiro.FromDecimal(200m),
                Status = StatusItemEstoque.Ativo,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await setupContext.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            var useCase = new ReporEstoqueUseCase(
                new ProdutoRepository(context),
                new ItemEstoqueRepository(context),
                new MovimentacaoEstoqueRepository(context),
                context);

            await useCase.ExecuteAsync(new ReporEstoqueCommand(
                empresaId,
                itemId,
                7,
                210m,
                450m,
                new DateTime(2026, 4, 4, 10, 0, 0, DateTimeKind.Utc),
                "Grafite",
                "Grafite",
                "Unico",
                "Reposicao fornecedor",
                "DOC-REPO-01",
                null,
                null));
        }

        await using (var assertContext = fixture.CreateDbContext())
        {
            var item = await assertContext.ItensEstoque.SingleAsync();
            var movimentacao = await assertContext.MovimentacoesEstoque.SingleAsync();

            item.QuantidadeAtual.Value.Should().Be(12);
            item.CustoUnitario.Valor.Should().Be(210m);
            item.PrecoVendaSugerido!.Valor.Should().Be(450m);
            movimentacao.Natureza.Should().Be(NaturezaMovimentacaoEstoque.Reposicao);
            movimentacao.Quantidade.Value.Should().Be(7);
            movimentacao.ValorTotal!.Valor.Should().Be(1470m);
        }
    }

    [Fact]
    public async Task RegistrarSaida_deve_persistir_venda_itens_venda_movimentacoes_e_baixar_estoque()
    {
        if (!fixture.IsAvailable) return;
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var item1Id = Guid.NewGuid();
        var item2Id = Guid.NewGuid();

        await using (var setupContext = fixture.CreateDbContext())
        {
            await SeedProdutoAsync(setupContext, empresaId, categoriaId, produtoId);
            setupContext.ItensEstoque.AddRange(
                new ItemEstoque
                {
                    Id = item1Id,
                    EmpresaId = empresaId,
                    ProdutoId = produtoId,
                    QuantidadeInicial = Quantidade.From(10),
                    QuantidadeAtual = Quantidade.From(10),
                    CustoUnitario = Dinheiro.FromDecimal(250m),
                    Status = StatusItemEstoque.Ativo,
                    EntradaEm = DateTime.UtcNow,
                    CriadoEm = DateTime.UtcNow,
                    AlteradoEm = DateTime.UtcNow,
                    DescricaoAnuncio = "Buds FE Grafite"
                },
                new ItemEstoque
                {
                    Id = item2Id,
                    EmpresaId = empresaId,
                    ProdutoId = produtoId,
                    QuantidadeInicial = Quantidade.From(5),
                    QuantidadeAtual = Quantidade.From(5),
                    CustoUnitario = Dinheiro.FromDecimal(250m),
                    Status = StatusItemEstoque.Ativo,
                    EntradaEm = DateTime.UtcNow,
                    CriadoEm = DateTime.UtcNow,
                    AlteradoEm = DateTime.UtcNow,
                    DescricaoAnuncio = "Buds FE Branco"
                });
            await setupContext.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            var useCase = new RegistrarSaidaEstoqueUseCase(
                new ProdutoRepository(context),
                new ItemEstoqueRepository(context),
                new VendaRepository(context),
                new ItemVendaRepository(context),
                new MovimentacaoEstoqueRepository(context),
                context);

            await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
                empresaId,
                [
                    new RegistrarSaidaEstoqueItemCommand(item1Id, 3, 399.90m, "Venda item 1"),
                    new RegistrarSaidaEstoqueItemCommand(item2Id, 2, 399.90m, "Venda item 2")
                ],
                new DateTime(2026, 4, 5, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 5, 10, 5, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 6, 10, 0, 0, DateTimeKind.Utc),
                "NF-123",
                NaturezaMovimentacaoEstoque.Venda,
                CanalVenda.MercadoLivre,
                "Pedido marketplace"));
        }

        await using (var assertContext = fixture.CreateDbContext())
        {
            var venda = await assertContext.Vendas.Include(v => v.ItensVenda).SingleAsync();
            var itens = await assertContext.ItensEstoque.OrderBy(i => i.Id).ToListAsync();
            var movimentacoes = await assertContext.MovimentacoesEstoque.OrderBy(m => m.Id).ToListAsync();

            venda.ValorTotal.Valor.Should().Be(1999.50m);
            venda.ItensVenda.Should().HaveCount(2);

            itens.Should().Contain(i => i.Id == item1Id && i.QuantidadeAtual.Value == 7);
            itens.Should().Contain(i => i.Id == item2Id && i.QuantidadeAtual.Value == 3);

            movimentacoes.Should().HaveCount(2);
            movimentacoes.Should().OnlyContain(m => m.Tipo == TipoMovimentacaoEstoque.Saida);
            movimentacoes.Should().OnlyContain(m => m.Natureza == NaturezaMovimentacaoEstoque.Venda);
        }
    }

    [Fact]
    public async Task RegistrarSaida_nao_deve_persistir_movimentacoes_quando_item_esta_bloqueado()
    {
        if (!fixture.IsAvailable) return;
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await using (var setupContext = fixture.CreateDbContext())
        {
            await SeedProdutoAsync(setupContext, empresaId, categoriaId, produtoId);
            setupContext.ItensEstoque.Add(new ItemEstoque
            {
                Id = itemId,
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                QuantidadeInicial = Quantidade.From(10),
                QuantidadeAtual = Quantidade.From(10),
                CustoUnitario = Dinheiro.FromDecimal(250m),
                Status = StatusItemEstoque.Bloqueado,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await setupContext.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            var useCase = new RegistrarSaidaEstoqueUseCase(
                new ProdutoRepository(context),
                new ItemEstoqueRepository(context),
                new VendaRepository(context),
                new ItemVendaRepository(context),
                new MovimentacaoEstoqueRepository(context),
                context);

            var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
                empresaId,
                [new RegistrarSaidaEstoqueItemCommand(itemId, 1, 399.90m, "Tentativa bloqueada")],
                new DateTime(2026, 4, 5, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 5, 10, 5, 0, DateTimeKind.Utc),
                null,
                null,
                NaturezaMovimentacaoEstoque.Venda,
                CanalVenda.MercadoLivre,
                "Item bloqueado"));

            await act.Should().ThrowAsync<ItemEstoqueBloqueadoException>();
        }

        await using (var assertContext = fixture.CreateDbContext())
        {
            var item = await assertContext.ItensEstoque.SingleAsync();
            var vendas = await assertContext.Vendas.CountAsync();
            var itensVenda = await assertContext.ItensVenda.CountAsync();
            var movimentacoes = await assertContext.MovimentacoesEstoque.CountAsync();

            item.QuantidadeAtual.Value.Should().Be(10);
            item.Status.Should().Be(StatusItemEstoque.Bloqueado);
            vendas.Should().Be(0);
            itensVenda.Should().Be(0);
            movimentacoes.Should().Be(0);
        }
    }

    [Fact]
    public async Task RegistrarSaida_nao_deve_persistir_quando_item_esta_vencido()
    {
        if (!fixture.IsAvailable) return;
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await using (var setupContext = fixture.CreateDbContext())
        {
            await SeedProdutoAsync(setupContext, empresaId, categoriaId, produtoId);
            setupContext.ItensEstoque.Add(new ItemEstoque
            {
                Id = itemId,
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                QuantidadeInicial = Quantidade.From(4),
                QuantidadeAtual = Quantidade.From(4),
                CustoUnitario = Dinheiro.FromDecimal(250m),
                Status = StatusItemEstoque.Ativo,
                EntradaEm = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                ValidadeEm = Validade.From(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await setupContext.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            var useCase = new RegistrarSaidaEstoqueUseCase(
                new ProdutoRepository(context),
                new ItemEstoqueRepository(context),
                new VendaRepository(context),
                new ItemVendaRepository(context),
                new MovimentacaoEstoqueRepository(context),
                context);

            var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
                empresaId,
                [new RegistrarSaidaEstoqueItemCommand(itemId, 1, 399.90m, "Tentativa vencida")],
                new DateTime(2026, 4, 5, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 5, 10, 5, 0, DateTimeKind.Utc),
                null,
                null,
                NaturezaMovimentacaoEstoque.Venda,
                CanalVenda.MercadoLivre,
                "Item vencido"));

            await act.Should().ThrowAsync<ItemEstoqueVencidoException>();
        }

        await using (var assertContext = fixture.CreateDbContext())
        {
            var item = await assertContext.ItensEstoque.SingleAsync();
            var vendas = await assertContext.Vendas.CountAsync();
            var movimentacoes = await assertContext.MovimentacoesEstoque.CountAsync();

            item.QuantidadeAtual.Value.Should().Be(4);
            vendas.Should().Be(0);
            movimentacoes.Should().Be(0);
        }
    }

    [Fact]
    public async Task ReporEstoque_nao_deve_persistir_quando_item_pertence_a_outra_empresa()
    {
        if (!fixture.IsAvailable) return;
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var outraEmpresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await using (var setupContext = fixture.CreateDbContext())
        {
            await SeedProdutoAsync(setupContext, outraEmpresaId, categoriaId, produtoId);
            setupContext.ItensEstoque.Add(new ItemEstoque
            {
                Id = itemId,
                EmpresaId = outraEmpresaId,
                ProdutoId = produtoId,
                QuantidadeInicial = Quantidade.From(5),
                QuantidadeAtual = Quantidade.From(5),
                CustoUnitario = Dinheiro.FromDecimal(200m),
                Status = StatusItemEstoque.Ativo,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await setupContext.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            var useCase = new ReporEstoqueUseCase(
                new ProdutoRepository(context),
                new ItemEstoqueRepository(context),
                new MovimentacaoEstoqueRepository(context),
                context);

            var act = () => useCase.ExecuteAsync(new ReporEstoqueCommand(
                empresaId,
                itemId,
                7,
                210m,
                450m,
                new DateTime(2026, 4, 4, 10, 0, 0, DateTimeKind.Utc),
                null,
                null,
                null,
                null,
                null,
                null,
                null));

            await act.Should().ThrowAsync<UseCaseValidationException>()
                .WithMessage("*nao pertence a empresa*");
        }

        await using (var assertContext = fixture.CreateDbContext())
        {
            var item = await assertContext.ItensEstoque.SingleAsync();
            var movimentacoes = await assertContext.MovimentacoesEstoque.CountAsync();

            item.QuantidadeAtual.Value.Should().Be(5);
            movimentacoes.Should().Be(0);
        }
    }

    [Fact]
    public async Task Atualizacoes_concorrentes_no_mesmo_item_devem_gerar_conflito()
    {
        if (!fixture.IsAvailable) return;
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await using (var setupContext = fixture.CreateDbContext())
        {
            await SeedProdutoAsync(setupContext, empresaId, categoriaId, produtoId);
            setupContext.ItensEstoque.Add(new ItemEstoque
            {
                Id = itemId,
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                QuantidadeInicial = Quantidade.From(10),
                QuantidadeAtual = Quantidade.From(10),
                CustoUnitario = Dinheiro.FromDecimal(250m),
                Status = StatusItemEstoque.Ativo,
                EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await setupContext.SaveChangesAsync();
        }

        await using var context1 = fixture.CreateDbContext();
        await using var context2 = fixture.CreateDbContext();

        var itemContext1 = await context1.ItensEstoque.SingleAsync(i => i.Id == itemId);
        var itemContext2 = await context2.ItensEstoque.SingleAsync(i => i.Id == itemId);

        itemContext1.RegistrarSaida(Quantidade.From(3), new DateTime(2026, 4, 5, 10, 0, 0, DateTimeKind.Utc), DateTime.UtcNow);
        await context1.SaveChangesAsync();

        itemContext2.RegistrarSaida(Quantidade.From(2), new DateTime(2026, 4, 5, 10, 1, 0, DateTimeKind.Utc), DateTime.UtcNow);

        var act = () => context2.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    private static async Task SeedProdutoAsync(DbContext context, Guid empresaId, Guid categoriaId, Guid produtoId)
    {
        context.Set<Empresa>().Add(new Empresa
        {
            Id = empresaId,
            Nome = "Empresa Teste",
            Documento = $"{Random.Shared.Next(100000, 999999)}",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });

        context.Set<Categoria>().Add(new Categoria
        {
            Id = categoriaId,
            EmpresaId = empresaId,
            Nome = "Audio",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });

        context.Set<Produto>().Add(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Galaxy Buds FE",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            PrecoReferencia = Dinheiro.FromDecimal(399.90m),
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
    }

    private sealed class GeradorDescricaoFake(string descricao) : IGeradorDescricaoAnuncio
    {
        public Task<string> GerarAsync(Produto produto, ProdutoVariacao? variacao, ItemEstoque? itemEstoque, string? instrucoesComplementares = null) =>
            Task.FromResult(descricao);
    }
}
