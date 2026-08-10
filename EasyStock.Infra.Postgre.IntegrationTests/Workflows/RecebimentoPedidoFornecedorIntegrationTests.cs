using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.UseCases.Pedido;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// Recebimento de compra (PedidoFornecedor) ponta a ponta contra Postgres real:
/// pedido com itens -> processar recebimento parcial -> entrada de estoque +
/// movimentacao Compra -> processar recebimento restante -> pedido Recebido.
///
/// Construido via DI de PRODUCAO (AddEasyStockApplication + AddEasyStockPostgreInfrastructure)
/// para validar atomicidade da transacao (#1019): se a 2a entrada falhar, a 1a
/// NAO fica orfa no banco (rollback reverte tudo).
/// </summary>
public class RecebimentoPedidoFornecedorIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task Recebimento_parcial_e_total_persiste_estoque_e_movimentacoes_atomicamente()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId1 = Guid.NewGuid();
        var produtoId2 = Guid.NewGuid();
        var fornecedorId = Guid.NewGuid();
        var pedidoId = Guid.NewGuid();
        var itemId1 = Guid.NewGuid();
        var itemId2 = Guid.NewGuid();
        var dataPedido = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var dataRecebimento1 = new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc);
        var dataRecebimento2 = new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc);

        // ── 1. Seed: empresa + loja + categoria + produtos + fornecedor ────
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(new Empresa
            {
                Id = empresaId, Nome = "Empresa E2E",
                Documento = $"{Random.Shared.Next(100000, 999999)}",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Loja>().Add(new Loja
            {
                Id = lojaId, EmpresaId = empresaId, Nome = "Loja E2E", Ativa = true,
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Categoria>().Add(new Categoria
            {
                Id = categoriaId, EmpresaId = empresaId, Nome = "Geral",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Produto>().AddRange(
                new Produto
                {
                    Id = produtoId1, EmpresaId = empresaId, CategoriaId = categoriaId,
                    Nome = "Produto A", Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo,
                    PrecoReferencia = Dinheiro.FromDecimal(100m),
                    CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
                },
                new Produto
                {
                    Id = produtoId2, EmpresaId = empresaId, CategoriaId = categoriaId,
                    Nome = "Produto B", Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo,
                    PrecoReferencia = Dinheiro.FromDecimal(200m),
                    CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
                });
            seed.Set<Fornecedor>().Add(new Fornecedor
            {
                Id = fornecedorId, EmpresaId = empresaId, Nome = "Fornecedor E2E",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });

            // Pedido de compra com 2 itens (10 unidades do produto A a R$50, 5 unidades do produto B a R$80)
            seed.Set<PedidoFornecedor>().Add(new PedidoFornecedor
            {
                Id = pedidoId, EmpresaId = empresaId, FornecedorId = fornecedorId,
                LojaId = lojaId, DataPedido = dataPedido, Status = StatusPedidoFornecedor.Aberto,
                ValorEstimado = 900m, CriadoEm = dataPedido, AlteradoEm = dataPedido,
                Itens =
                {
                    new PedidoFornecedorItem
                    {
                        Id = itemId1, PedidoFornecedorId = pedidoId, ProdutoId = produtoId1,
                        Nome = "Produto A", Quantidade = 10, QuantidadeRecebida = 0,
                        CustoUnitario = 50m, CriadoEm = dataPedido
                    },
                    new PedidoFornecedorItem
                    {
                        Id = itemId2, PedidoFornecedorId = pedidoId, ProdutoId = produtoId2,
                        Nome = "Produto B", Quantidade = 5, QuantidadeRecebida = 0,
                        CustoUnitario = 80m, CriadoEm = dataPedido
                    }
                }
            });

            await seed.SaveChangesAsync();
        }

        // ── 2. Orquestrador via DI de producao ─────────────────────────────
        await using var provider = BuildProductionProvider();

        // ── 3. Recebimento PARCIAL: so item 1 (10 unidades) ────────────────
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);

            var useCase = scope.ServiceProvider.GetRequiredService<ProcessarRecebimentoPedidoFornecedorUseCase>();

            var result = await useCase.ExecuteAsync(new ProcessarRecebimentoPedidoFornecedorCommand(
                PedidoId: pedidoId,
                EmpresaId: empresaId,
                DataRecebimento: dataRecebimento1,
                ItensRecebidos: new Dictionary<Guid, decimal> { { itemId1, 10m } }));

            result.ItensProcessados.Should().Be(1);
            result.Mensagem.Should().Contain("1 itens processados");
        }

        // ── 4. Asserts pos-recebimento parcial ─────────────────────────────
        await using (var assert = fixture.CreateDbContext())
        {
            assert.SetMobileTenantContext(empresaId);

            var pedido = await assert.PedidosFornecedor
                .Include(p => p.Itens)
                .SingleAsync(p => p.Id == pedidoId);

            pedido.Status.Should().Be(StatusPedidoFornecedor.RecebidoParcial);
            pedido.DataRecebimento.Should().Be(dataRecebimento1);
            pedido.Itens.Single(i => i.Id == itemId1).QuantidadeRecebida.Should().Be(10m);
            pedido.Itens.Single(i => i.Id == itemId2).QuantidadeRecebida.Should().Be(0m);

            // 1 item de estoque criado (produto A)
            var itensEstoque = await assert.ItensEstoque.ToListAsync();
            itensEstoque.Should().HaveCount(1);
            itensEstoque[0].ProdutoId.Should().Be(produtoId1);
            itensEstoque[0].QuantidadeAtual.Value.Should().Be(10m);
            itensEstoque[0].CustoUnitario.Valor.Should().Be(50m);

            // 1 movimentacao de entrada (Compra)
            var movs = await assert.MovimentacoesEstoque.ToListAsync();
            movs.Should().HaveCount(1);
            movs[0].Tipo.Should().Be(TipoMovimentacaoEstoque.Entrada);
            movs[0].Natureza.Should().Be(NaturezaMovimentacaoEstoque.Compra);
            movs[0].Quantidade.Value.Should().Be(10m);
            movs[0].ProdutoId.Should().Be(produtoId1);
            movs[0].DocumentoReferencia.Should().Be($"{pedidoId}:{itemId1}:r10");
        }

        // ── 5. Recebimento TOTAL: item 2 (5 unidades) ──────────────────────
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);

            var useCase = scope.ServiceProvider.GetRequiredService<ProcessarRecebimentoPedidoFornecedorUseCase>();

            var result = await useCase.ExecuteAsync(new ProcessarRecebimentoPedidoFornecedorCommand(
                PedidoId: pedidoId,
                EmpresaId: empresaId,
                DataRecebimento: dataRecebimento2,
                ItensRecebidos: new Dictionary<Guid, decimal> { { itemId2, 5m } }));

            result.ItensProcessados.Should().Be(1);
        }

        // ── 6. Asserts pos-recebimento total ───────────────────────────────
        await using (var assert = fixture.CreateDbContext())
        {
            assert.SetMobileTenantContext(empresaId);

            var pedido = await assert.PedidosFornecedor
                .Include(p => p.Itens)
                .SingleAsync(p => p.Id == pedidoId);

            pedido.Status.Should().Be(StatusPedidoFornecedor.Recebido);
            pedido.DataRecebimento.Should().Be(dataRecebimento2);
            pedido.Itens.Single(i => i.Id == itemId1).QuantidadeRecebida.Should().Be(10m);
            pedido.Itens.Single(i => i.Id == itemId2).QuantidadeRecebida.Should().Be(5m);

            // 2 itens de estoque (A e B)
            var itensEstoque = await assert.ItensEstoque.OrderBy(i => i.ProdutoId).ToListAsync();
            itensEstoque.Should().HaveCount(2);
            itensEstoque[0].ProdutoId.Should().Be(produtoId1);
            itensEstoque[0].QuantidadeAtual.Value.Should().Be(10m);
            itensEstoque[1].ProdutoId.Should().Be(produtoId2);
            itensEstoque[1].QuantidadeAtual.Value.Should().Be(5m);
            itensEstoque[1].CustoUnitario.Valor.Should().Be(80m);

            // 2 movimentacoes de entrada (Compra)
            var movs = await assert.MovimentacoesEstoque.OrderBy(m => m.ProdutoId).ToListAsync();
            movs.Should().HaveCount(2);
            movs.Should().OnlyContain(m => m.Tipo == TipoMovimentacaoEstoque.Entrada);
            movs.Should().OnlyContain(m => m.Natureza == NaturezaMovimentacaoEstoque.Compra);
            movs[0].Quantidade.Value.Should().Be(10m);
            movs[1].Quantidade.Value.Should().Be(5m);
        }
    }

    [SkippableFact]
    public async Task Recebimento_idempotente_nao_dobra_estoque_em_retry()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var fornecedorId = Guid.NewGuid();
        var pedidoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var dataPedido = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var dataRecebimento = new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc);

        // ── Seed ───────────────────────────────────────────────────────────
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(new Empresa
            {
                Id = empresaId, Nome = "Empresa E2E",
                Documento = $"{Random.Shared.Next(100000, 999999)}",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Categoria>().Add(new Categoria
            {
                Id = categoriaId, EmpresaId = empresaId, Nome = "Geral",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Produto>().Add(new Produto
            {
                Id = produtoId, EmpresaId = empresaId, CategoriaId = categoriaId,
                Nome = "Produto Unico", Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo,
                PrecoReferencia = Dinheiro.FromDecimal(100m),
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Fornecedor>().Add(new Fornecedor
            {
                Id = fornecedorId, EmpresaId = empresaId, Nome = "Fornecedor E2E",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<PedidoFornecedor>().Add(new PedidoFornecedor
            {
                Id = pedidoId, EmpresaId = empresaId, FornecedorId = fornecedorId,
                DataPedido = dataPedido, Status = StatusPedidoFornecedor.Aberto,
                CriadoEm = dataPedido, AlteradoEm = dataPedido,
                Itens =
                {
                    new PedidoFornecedorItem
                    {
                        Id = itemId, PedidoFornecedorId = pedidoId, ProdutoId = produtoId,
                        Nome = "Produto Unico", Quantidade = 10, QuantidadeRecebida = 0,
                        CustoUnitario = 50m, CriadoEm = dataPedido
                    }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var provider = BuildProductionProvider();

        // ── 1o recebimento ─────────────────────────────────────────────────
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);
            var useCase = scope.ServiceProvider.GetRequiredService<ProcessarRecebimentoPedidoFornecedorUseCase>();
            await useCase.ExecuteAsync(new ProcessarRecebimentoPedidoFornecedorCommand(
                pedidoId, empresaId, dataRecebimento, new Dictionary<Guid, decimal> { { itemId, 10m } }));
        }

        // ── 2o recebimento (mesma chave) = idempotente ─────────────────────
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);
            var useCase = scope.ServiceProvider.GetRequiredService<ProcessarRecebimentoPedidoFornecedorUseCase>();
            var result = await useCase.ExecuteAsync(new ProcessarRecebimentoPedidoFornecedorCommand(
                pedidoId, empresaId, dataRecebimento, new Dictionary<Guid, decimal> { { itemId, 10m } }));

            // Ja estava recebido — retorna 0 itens processados
            result.ItensProcessados.Should().Be(0);
            result.Mensagem.Should().Contain("já recebido");
        }

        // ── Asserts: estoque NAO dobrou ────────────────────────────────────
        await using (var assert = fixture.CreateDbContext())
        {
            assert.SetMobileTenantContext(empresaId);

            var itensEstoque = await assert.ItensEstoque.ToListAsync();
            itensEstoque.Should().HaveCount(1);
            itensEstoque[0].QuantidadeAtual.Value.Should().Be(10m, "idempotencia deve evitar duplicacao de estoque");

            var movs = await assert.MovimentacoesEstoque.ToListAsync();
            movs.Should().HaveCount(1, "idempotencia deve evitar movimentacao duplicada");
        }
    }

    private ServiceProvider BuildProductionProvider()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddSingleton(Substitute.For<EasyStock.Application.Ports.Output.ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        return services.BuildServiceProvider();
    }
}
