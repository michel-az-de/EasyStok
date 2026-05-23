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
    [SkippableFact]
    public async Task RegistrarEntrada_deve_persistir_item_e_movimentacao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
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
            context.SetMobileTenantContext(empresaId);
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
            assertContext.SetMobileTenantContext(empresaId);
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

    [SkippableFact]
    public async Task ReporEstoque_deve_atualizar_quantidade_e_gerar_movimentacao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
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
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await setupContext.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            context.SetMobileTenantContext(empresaId);
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
            assertContext.SetMobileTenantContext(empresaId);
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

    [SkippableFact]
    public async Task RegistrarSaida_deve_persistir_venda_itens_venda_movimentacoes_e_baixar_estoque()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
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
                    Status = StatusItemEstoque.Ok,
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
                    Status = StatusItemEstoque.Ok,
                    EntradaEm = DateTime.UtcNow,
                    CriadoEm = DateTime.UtcNow,
                    AlteradoEm = DateTime.UtcNow,
                    DescricaoAnuncio = "Buds FE Branco"
                });
            await setupContext.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            context.SetMobileTenantContext(empresaId);
            var useCase = new RegistrarSaidaEstoqueUseCase(
                new ProdutoRepository(context),
                new ItemEstoqueRepository(context),
                new VendaRepository(context),
                new ItemVendaRepository(context),
                new MovimentacaoEstoqueRepository(context),
                context,
                NullLogger<RegistrarSaidaEstoqueUseCase>.Instance);

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
            assertContext.SetMobileTenantContext(empresaId);
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

    [SkippableFact]
    public async Task RegistrarSaida_deve_consumir_lotes_em_fifo_automaticamente_no_postgre()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var loteAntigoId = Guid.NewGuid();
        var loteNovoId = Guid.NewGuid();
        var entradaBase = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc);

        await using (var setupContext = fixture.CreateDbContext())
        {
            await SeedProdutoAsync(setupContext, empresaId, categoriaId, produtoId);
            setupContext.ItensEstoque.AddRange(
                new ItemEstoque
                {
                    Id = loteAntigoId,
                    EmpresaId = empresaId,
                    ProdutoId = produtoId,
                    QuantidadeInicial = Quantidade.From(10),
                    QuantidadeAtual = Quantidade.From(10),
                    QuantidadeMinima = 5,
                    CustoUnitario = Dinheiro.FromDecimal(250m),
                    // Validades distintas tornam a ordem FEFO deterministica
                    // (loteAntigo expira primeiro -> consumido antes). Com
                    // ValidadeEm=null em ambos, o tie-break por EntradaEm
                    // dentro do FromSqlRaw + FOR UPDATE nao sobrevive ao
                    // wrap externo da composicao com o filtro global do EF.
                    ValidadeEm = Validade.From(entradaBase.AddMonths(2)),
                    Status = StatusItemEstoque.Ok,
                    EntradaEm = entradaBase,
                    UltimaMovimentacaoEm = entradaBase,
                    CriadoEm = entradaBase,
                    AlteradoEm = entradaBase,
                    DescricaoAnuncio = "Lote antigo"
                },
                new ItemEstoque
                {
                    Id = loteNovoId,
                    EmpresaId = empresaId,
                    ProdutoId = produtoId,
                    QuantidadeInicial = Quantidade.From(5),
                    QuantidadeAtual = Quantidade.From(5),
                    QuantidadeMinima = 5,
                    CustoUnitario = Dinheiro.FromDecimal(255m),
                    ValidadeEm = Validade.From(entradaBase.AddMonths(6)), // loteNovo: validade mais distante -> FEFO sai depois
                    Status = StatusItemEstoque.Ok,
                    EntradaEm = entradaBase.AddDays(2),
                    UltimaMovimentacaoEm = entradaBase.AddDays(2),
                    CriadoEm = entradaBase.AddDays(2),
                    AlteradoEm = entradaBase.AddDays(2),
                    DescricaoAnuncio = "Lote novo"
                });
            await setupContext.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            context.SetMobileTenantContext(empresaId);
            var useCase = new RegistrarSaidaEstoqueUseCase(
                new ProdutoRepository(context),
                new ItemEstoqueRepository(context),
                new VendaRepository(context),
                new ItemVendaRepository(context),
                new MovimentacaoEstoqueRepository(context),
                context,
                NullLogger<RegistrarSaidaEstoqueUseCase>.Instance);

            var result = await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
                empresaId,
                [new RegistrarSaidaEstoqueItemCommand(produtoId, null, 12, 399.90m, "Venda FIFO Postgre")],
                new DateTime(2026, 4, 5, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 5, 10, 5, 0, DateTimeKind.Utc),
                null,
                "NF-FIFO-POSTGRE",
                NaturezaMovimentacaoEstoque.Venda,
                CanalVenda.MercadoLivre,
                "Saida FIFO"));

            // Ordem FEFO deterministica garantida por .IgnoreQueryFilters() apos o
            // FromSqlRaw em ItemEstoqueRepository.GetLotesDisponiveisParaSaidaAsync
            // (sem isso, o wrap em subquery do filtro global descartava o ORDER BY
            // interno -> ordem nao-deterministica). loteAntigo (validade mais
            // proxima) esgota primeiro (10 unidades), loteNovo absorve o resto (2).
            result.Itens.Should().HaveCount(2);
            result.Itens.Select(i => i.QuantidadeSaida).Should().Equal(10, 2);
            result.Itens.Select(i => i.ItemEstoqueId).Should().Equal(loteAntigoId, loteNovoId);
        }

        await using (var assertContext = fixture.CreateDbContext())
        {
            assertContext.SetMobileTenantContext(empresaId);
            var lotes = await assertContext.ItensEstoque
                .OrderBy(i => i.EntradaEm)
                .ToListAsync();
            var venda = await assertContext.Vendas.Include(v => v.ItensVenda).SingleAsync();
            var movimentacoes = await assertContext.MovimentacoesEstoque
                .OrderBy(m => m.DataMovimentacao)
                .ToListAsync();

            // lotes ordenado por EntradaEm asc: [0]=loteAntigo (esgotado, qty 0),
            // [1]=loteNovo (qty 3, Warn). Ordem FEFO deterministica garantida pelo
            // fix em ItemEstoqueRepository.GetLotesDisponiveisParaSaidaAsync.
            lotes.Should().HaveCount(2);
            lotes[0].Id.Should().Be(loteAntigoId);
            lotes[0].QuantidadeAtual.Value.Should().Be(0);
            lotes[0].Status.Should().Be(StatusItemEstoque.Esgotado);
            lotes[1].Id.Should().Be(loteNovoId);
            lotes[1].QuantidadeAtual.Value.Should().Be(3);
            lotes[1].Status.Should().Be(StatusItemEstoque.Warn);
            lotes.Should().OnlyContain(i => i.VelocidadeSaidaDiaria > 0m);
            lotes.Should().OnlyContain(i => i.PrevisaoZeramentoDias.HasValue);

            venda.ItensVenda.Should().HaveCount(2);
            movimentacoes.Should().HaveCount(2);
            // movimentacoes ordenadas por DataMovimentacao (mesmo timestamp p/ ambas)
            // -> ordem instavel entre execucoes; usar BeEquivalentTo (set-equality).
            movimentacoes.Select(m => m.ItemEstoqueId).Should().BeEquivalentTo(new[] { loteAntigoId, loteNovoId });
            movimentacoes.Sum(m => m.Quantidade.Value).Should().Be(12);
        }
    }

    [SkippableFact]
    public async Task RegistrarSaida_nao_deve_persistir_movimentacoes_quando_item_esta_bloqueado()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
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
            context.SetMobileTenantContext(empresaId);
            var useCase = new RegistrarSaidaEstoqueUseCase(
                new ProdutoRepository(context),
                new ItemEstoqueRepository(context),
                new VendaRepository(context),
                new ItemVendaRepository(context),
                new MovimentacaoEstoqueRepository(context),
                context,
                NullLogger<RegistrarSaidaEstoqueUseCase>.Instance);

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
            assertContext.SetMobileTenantContext(empresaId);
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

    [SkippableFact]
    public async Task RegistrarSaida_nao_deve_persistir_quando_item_esta_vencido()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
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
                Status = StatusItemEstoque.Ok,
                EntradaEm = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                ValidadeEm = Validade.From(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await setupContext.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            context.SetMobileTenantContext(empresaId);
            var useCase = new RegistrarSaidaEstoqueUseCase(
                new ProdutoRepository(context),
                new ItemEstoqueRepository(context),
                new VendaRepository(context),
                new ItemVendaRepository(context),
                new MovimentacaoEstoqueRepository(context),
                context,
                NullLogger<RegistrarSaidaEstoqueUseCase>.Instance);

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
            assertContext.SetMobileTenantContext(empresaId);
            var item = await assertContext.ItensEstoque.SingleAsync();
            var vendas = await assertContext.Vendas.CountAsync();
            var movimentacoes = await assertContext.MovimentacoesEstoque.CountAsync();

            item.QuantidadeAtual.Value.Should().Be(4);
            vendas.Should().Be(0);
            movimentacoes.Should().Be(0);
        }
    }

    [SkippableFact]
    public async Task ReporEstoque_nao_deve_persistir_quando_item_pertence_a_outra_empresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
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
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await setupContext.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            // O item pertence a outraEmpresaId; o filtro global precisa enxergar
            // a empresa dona para o caso de uso alcancar a guarda de propriedade
            // (do contrario GetByIdAsync filtra o item e o erro vira "nao encontrado").
            context.SetMobileTenantContext(outraEmpresaId);
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
            assertContext.SetMobileTenantContext(outraEmpresaId);
            var item = await assertContext.ItensEstoque.SingleAsync();
            var movimentacoes = await assertContext.MovimentacoesEstoque.CountAsync();

            item.QuantidadeAtual.Value.Should().Be(5);
            movimentacoes.Should().Be(0);
        }
    }

    [SkippableFact]
    public async Task Atualizacoes_concorrentes_no_mesmo_item_devem_gerar_conflito()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
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
                Status = StatusItemEstoque.Ok,
                EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await setupContext.SaveChangesAsync();
        }

        await using var context1 = fixture.CreateDbContext();
        await using var context2 = fixture.CreateDbContext();

        context1.SetMobileTenantContext(empresaId);
        context2.SetMobileTenantContext(empresaId);

        var itemContext1 = await context1.ItensEstoque.SingleAsync(i => i.Id == itemId);
        var itemContext2 = await context2.ItensEstoque.SingleAsync(i => i.Id == itemId);

        itemContext1.RegistrarSaida(Quantidade.From(3), new DateTime(2026, 4, 5, 10, 0, 0, DateTimeKind.Utc), DateTime.UtcNow);
        await context1.SaveChangesAsync();

        itemContext2.RegistrarSaida(Quantidade.From(2), new DateTime(2026, 4, 5, 10, 1, 0, DateTimeKind.Utc), DateTime.UtcNow);

        var act = () => context2.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [SkippableFact]
    public async Task Queries_de_inteligencia_devem_respeitar_paginacao_e_filtros()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        await using (var setupContext = fixture.CreateDbContext())
        {
            await SeedProdutoAsync(setupContext, empresaId, categoriaId, produtoId);
            // Design das 5 linhas (intencao do teste original, agora coerente):
            //  - 3 itens "baixo" (qty <= 5, e tambem < 5 para SugestaoReposicao)
            //  - 1 item "normal" (qty > 5, com movimentacao recente)
            //  - 1 item "parado" exclusivo (qty > 5, UltimaMovimentacao antiga)
            //  - 1 dos "baixo" tambem tem validade proxima
            setupContext.ItensEstoque.AddRange(
                new ItemEstoque
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId,
                    ProdutoId = produtoId,
                    QuantidadeInicial = Quantidade.From(3),
                    QuantidadeAtual = Quantidade.From(3), // Baixo (qty<5)
                    CustoUnitario = Dinheiro.FromDecimal(10m),
                    Status = StatusItemEstoque.Ok,
                    EntradaEm = DateTime.UtcNow,
                    UltimaMovimentacaoEm = DateTime.UtcNow.AddDays(-1),
                    CriadoEm = DateTime.UtcNow,
                    AlteradoEm = DateTime.UtcNow
                },
                new ItemEstoque
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId,
                    ProdutoId = produtoId,
                    QuantidadeInicial = Quantidade.From(2),
                    QuantidadeAtual = Quantidade.From(2), // Baixo (qty<5)
                    CustoUnitario = Dinheiro.FromDecimal(10m),
                    Status = StatusItemEstoque.Ok,
                    EntradaEm = DateTime.UtcNow,
                    UltimaMovimentacaoEm = DateTime.UtcNow.AddDays(-1),
                    CriadoEm = DateTime.UtcNow,
                    AlteradoEm = DateTime.UtcNow
                },
                new ItemEstoque
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId,
                    ProdutoId = produtoId,
                    QuantidadeInicial = Quantidade.From(10),
                    QuantidadeAtual = Quantidade.From(10), // Normal (qty>5)
                    CustoUnitario = Dinheiro.FromDecimal(10m),
                    Status = StatusItemEstoque.Ok,
                    EntradaEm = DateTime.UtcNow,
                    UltimaMovimentacaoEm = DateTime.UtcNow.AddDays(-1),
                    CriadoEm = DateTime.UtcNow,
                    AlteradoEm = DateTime.UtcNow
                },
                new ItemEstoque
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId,
                    ProdutoId = produtoId,
                    QuantidadeInicial = Quantidade.From(1),
                    QuantidadeAtual = Quantidade.From(1), // Baixo + proximo vencimento
                    CustoUnitario = Dinheiro.FromDecimal(10m),
                    ValidadeEm = Validade.From(DateTime.UtcNow.AddDays(20)),
                    Status = StatusItemEstoque.Ok,
                    EntradaEm = DateTime.UtcNow,
                    UltimaMovimentacaoEm = DateTime.UtcNow.AddDays(-1),
                    CriadoEm = DateTime.UtcNow,
                    AlteradoEm = DateTime.UtcNow
                },
                new ItemEstoque
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId,
                    ProdutoId = produtoId,
                    QuantidadeInicial = Quantidade.From(8),
                    QuantidadeAtual = Quantidade.From(8), // Parado exclusivo (qty>5)
                    CustoUnitario = Dinheiro.FromDecimal(10m),
                    UltimaMovimentacaoEm = DateTime.UtcNow.AddDays(-100),
                    Status = StatusItemEstoque.Ok,
                    EntradaEm = DateTime.UtcNow,
                    CriadoEm = DateTime.UtcNow,
                    AlteradoEm = DateTime.UtcNow
                });
            await setupContext.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            context.SetMobileTenantContext(empresaId);
            var repository = new ItemEstoqueRepository(context);

            // Teste estoque baixo
            var (baixo, totalBaixo) = await repository.GetEstoqueBaixoAsync(empresaId, 5, 1, 2);
            baixo.Should().HaveCount(2);
            totalBaixo.Should().Be(3); // 3 itens com <=5
            baixo.Should().AllSatisfy(i => i.QuantidadeAtual.Value.Should().BeLessThanOrEqualTo(5));

            // Teste próximo vencimento
            var (proximos, totalProximos) = await repository.GetProximoVencimentoAsync(empresaId, 30, 1, 10);
            proximos.Should().ContainSingle();
            totalProximos.Should().Be(1);
            proximos.Single().ValidadeEm.Should().NotBeNull();

            // Teste itens parados
            var (parados, totalParados) = await repository.GetItensParadosAsync(empresaId, 90, 1, 10);
            parados.Should().ContainSingle();
            totalParados.Should().Be(1);
            parados.Single().Should().Match<ItemEstoque>(i => !i.UltimaMovimentacaoEm.HasValue || i.UltimaMovimentacaoEm < DateTime.UtcNow.AddDays(-90));

            // Teste sugestão reposição
            // Args posicionais: (empresaId, limiteQuantidade=5, page=1, pageSize=10).
            var (sugestoes, totalSugestoes) = await repository.GetSugestaoReposicaoAsync(empresaId, 5, 1, 10);
            sugestoes.Should().HaveCount(3);
            totalSugestoes.Should().Be(3);
            sugestoes.Should().AllSatisfy(i => i.QuantidadeAtual.Value.Should().BeLessThan(5));
        }
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

    [SkippableFact]
    public async Task Concorrencia_em_registrar_saida_deve_manter_saldo_consistente()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
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
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await setupContext.SaveChangesAsync();
        }

        // Simular duas saídas concorrentes
        var task1 = Task.Run(async () =>
        {
            await using var context = fixture.CreateDbContext();
            context.SetMobileTenantContext(empresaId);
            var useCase = new RegistrarSaidaEstoqueUseCase(
                new ProdutoRepository(context),
                new ItemEstoqueRepository(context),
                new VendaRepository(context),
                new ItemVendaRepository(context),
                new MovimentacaoEstoqueRepository(context),
                context,
                NullLogger<RegistrarSaidaEstoqueUseCase>.Instance);

            await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
                empresaId,
                [new RegistrarSaidaEstoqueItemCommand(itemId, 3, 399.90m, "Saida 1")],
                DateTime.UtcNow,
                DateTime.UtcNow,
                null,
                null,
                NaturezaMovimentacaoEstoque.Venda,
                CanalVenda.MercadoLivre,
                "Concorrente 1"));
        });

        var task2 = Task.Run(async () =>
        {
            await Task.Delay(100); // Pequeno delay para garantir concorrência
            await using var context = fixture.CreateDbContext();
            context.SetMobileTenantContext(empresaId);
            var useCase = new RegistrarSaidaEstoqueUseCase(
                new ProdutoRepository(context),
                new ItemEstoqueRepository(context),
                new VendaRepository(context),
                new ItemVendaRepository(context),
                new MovimentacaoEstoqueRepository(context),
                context,
                NullLogger<RegistrarSaidaEstoqueUseCase>.Instance);

            await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
                empresaId,
                [new RegistrarSaidaEstoqueItemCommand(itemId, 2, 399.90m, "Saida 2")],
                DateTime.UtcNow,
                DateTime.UtcNow,
                null,
                null,
                NaturezaMovimentacaoEstoque.Venda,
                CanalVenda.MercadoLivre,
                "Concorrente 2"));
        });

        // Com FOR UPDATE pessimista, AMBAS as saidas serializam e completam:
        // o lote tem 10 unidades, sobra para os dois comandos (3 + 2 = 5).
        await Task.WhenAll(task1, task2);

        await using (var assertContext = fixture.CreateDbContext())
        {
            assertContext.SetMobileTenantContext(empresaId);
            var item = await assertContext.ItensEstoque.SingleAsync();
            var vendas = await assertContext.Vendas.ToListAsync();
            var movimentacoes = await assertContext.MovimentacoesEstoque.ToListAsync();

            // venda1 baixa 3 (10->7), venda2 baixa 2 (7->5). Saldo final == 5.
            // Nota: assert direto em vez de "10 - vendas.SelectMany(v => v.ItensVenda)"
            // porque vendas nao foi carregada com Include(v => v.ItensVenda),
            // entao a colecao vinha vazia e o calculo dava 10.
            vendas.Should().HaveCount(2);
            movimentacoes.Should().HaveCount(2);
            item.QuantidadeAtual.Value.Should().Be(5, "10 - 3 (venda1) - 2 (venda2) = 5");
        }
    }

    /// <summary>
    /// Fluxo ponta a ponta: Entrada aumenta estoque → Saída baixa estoque →
    /// Analytics reflete quantidade, movimentações e receita corretamente.
    /// </summary>
    [SkippableFact]
    public async Task FluxoPontaAPonta_Entrada_Saida_Estoque_Analytics_devem_ser_coerentes()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        // Datas relativas a "agora": dashboard.ReceitaEstimadaPeriodo usa janela
        // rolante de 30 dias contados de DateTime.UtcNow; datas fixas no passado
        // (April/2026) cairiam fora dessa janela e a receita viria 0.
        var dataEntrada = DateTime.UtcNow.AddDays(-10);
        var dataSaida = DateTime.UtcNow.AddDays(-5);

        // ── 1. Seed produto ────────────────────────────────────────────────
        await using (var ctx = fixture.CreateDbContext())
            await SeedProdutoAsync(ctx, empresaId, categoriaId, produtoId);

        // ── 2. Registrar Entrada (10 unidades a R$ 50) ─────────────────────
        Guid itemEstoqueId;
        await using (var ctx = fixture.CreateDbContext())
        {
            ctx.SetMobileTenantContext(empresaId);
            var entradaUseCase = new RegistrarEntradaEstoqueUseCase(
                new ProdutoRepository(ctx),
                new ProdutoVariacaoRepository(ctx),
                new ItemEstoqueRepository(ctx),
                new MovimentacaoEstoqueRepository(ctx),
                ctx,
                NullLogger<RegistrarEntradaEstoqueUseCase>.Instance);

            var result = await entradaUseCase.ExecuteAsync(new RegistrarEntradaEstoqueCommand(
                EmpresaId: empresaId,
                ProdutoId: produtoId,
                ProdutoVariacaoId: null,
                Quantidade: 10,
                CustoUnitario: 50m,
                PrecoVendaSugerido: 100m,
                DataEntrada: dataEntrada,
                Natureza: NaturezaMovimentacaoEstoque.Compra,
                CodigoInterno: null, CodigoLote: null, CodigoMarketplace: null,
                VariacaoDescricao: null, Cor: null, Tamanho: null, FornecedorNome: null,
                Validade: null, Observacoes: null, DescricaoAnuncio: null,
                DocumentoReferencia: null, DimensoesReais: null,
                InstrucoesGeracaoDescricao: null));

            itemEstoqueId = result.ItemEstoqueId;
        }

        // ── 3. Verificar estoque após entrada ──────────────────────────────
        await using (var ctx = fixture.CreateDbContext())
        {
            ctx.SetMobileTenantContext(empresaId);
            var item = await ctx.ItensEstoque.SingleAsync(i => i.Id == itemEstoqueId);
            item.QuantidadeAtual.Value.Should().Be(10, "entrada de 10 deve aumentar estoque para 10");
            item.ProdutoId.Should().Be(produtoId);
            item.EmpresaId.Should().Be(empresaId);

            var movEntrada = await ctx.MovimentacoesEstoque.SingleAsync();
            movEntrada.Tipo.Should().Be(TipoMovimentacaoEstoque.Entrada);
            movEntrada.Quantidade.Value.Should().Be(10);
            movEntrada.ValorTotal!.Valor.Should().Be(500m); // 10 * 50
        }

        // ── 4. Registrar Saída (4 unidades a R$ 100) ──────────────────────
        await using (var ctx = fixture.CreateDbContext())
        {
            ctx.SetMobileTenantContext(empresaId);
            var saidaUseCase = new RegistrarSaidaEstoqueUseCase(
                new ProdutoRepository(ctx),
                new ItemEstoqueRepository(ctx),
                new VendaRepository(ctx),
                new ItemVendaRepository(ctx),
                new MovimentacaoEstoqueRepository(ctx),
                ctx,
                NullLogger<RegistrarSaidaEstoqueUseCase>.Instance);

            await saidaUseCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
                EmpresaId: empresaId,
                Itens: [new RegistrarSaidaEstoqueItemCommand(itemEstoqueId, 4, 100m, "Venda ponta-a-ponta")],
                DataVenda: dataSaida,
                DataSaida: dataSaida.AddMinutes(5),
                DataEnvio: null,
                NotaFiscal: "NF-E2E",
                Natureza: NaturezaMovimentacaoEstoque.Venda,
                Canal: CanalVenda.MercadoLivre,
                Observacoes: "Teste e2e"));
        }

        // ── 5. Verificar estoque após saída ────────────────────────────────
        await using (var ctx = fixture.CreateDbContext())
        {
            ctx.SetMobileTenantContext(empresaId);
            var item = await ctx.ItensEstoque.SingleAsync(i => i.Id == itemEstoqueId);
            item.QuantidadeAtual.Value.Should().Be(6, "saída de 4 deve deixar 6 em estoque");

            var movs = await ctx.MovimentacoesEstoque.OrderBy(m => m.Tipo).ToListAsync();
            movs.Should().HaveCount(2);
            movs.Should().Contain(m => m.Tipo == TipoMovimentacaoEstoque.Entrada && m.Quantidade.Value == 10);
            movs.Should().Contain(m => m.Tipo == TipoMovimentacaoEstoque.Saida && m.Quantidade.Value == 4);

            var saida = movs.Single(m => m.Tipo == TipoMovimentacaoEstoque.Saida);
            saida.ValorTotal!.Valor.Should().Be(400m, "4 * R$100 = R$400 de receita");
            saida.Natureza.Should().Be(NaturezaMovimentacaoEstoque.Venda);

            var venda = await ctx.Vendas.SingleAsync();
            venda.EmpresaId.Should().Be(empresaId);
            venda.ValorTotal.Valor.Should().Be(400m);
        }

        // ── 6. Analytics: dashboard deve refletir estoque e receita ────────
        await using (var ctx = fixture.CreateDbContext())
        {
            ctx.SetMobileTenantContext(empresaId);
            var analytics = new AnalyticsRepository(ctx);
            var dashboard = await analytics.GetDashboardResumoAsync(empresaId, periodoDias: 30);

            dashboard.QuantidadeTotalEmEstoque.Should().Be(6, "dashboard deve mostrar 6 unidades restantes");
            dashboard.ReceitaEstimadaPeriodo.Should().Be(400m, "dashboard deve mostrar receita da saída");
            dashboard.TotalSkus.Should().Be(1);
        }

        // ── 7. Analytics: movimentações devem listar entrada e saída ───────
        await using (var ctx = fixture.CreateDbContext())
        {
            ctx.SetMobileTenantContext(empresaId);
            var analytics = new AnalyticsRepository(ctx);
            var movs = await analytics.GetMovimentacoesResumoAsync(
                empresaId,
                de: DateTime.UtcNow.AddDays(-30),
                ate: DateTime.UtcNow.AddDays(1));

            movs.Should().Contain(m => m.Tipo == TipoMovimentacaoEstoque.Entrada && m.QuantidadeTotal == 10);
            movs.Should().Contain(m => m.Tipo == TipoMovimentacaoEstoque.Saida && m.QuantidadeTotal == 4);
            movs.Should().Contain(m => m.Tipo == TipoMovimentacaoEstoque.Saida && m.ValorTotal == 400m);
        }

        // ── 8. Analytics: receita por período deve refletir a venda ────────
        await using (var ctx = fixture.CreateDbContext())
        {
            ctx.SetMobileTenantContext(empresaId);
            var analytics = new AnalyticsRepository(ctx);
            var receita = await analytics.GetReceitaPorPeriodoAsync(empresaId, meses: 12);

            receita.Should().Contain(r => r.Ano == dataSaida.Year && r.Mes == dataSaida.Month && r.TotalItensVendidos == 4);
        }
    }
}
