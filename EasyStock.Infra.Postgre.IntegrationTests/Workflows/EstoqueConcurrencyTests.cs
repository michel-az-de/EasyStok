using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// Testa transações pessimistas e locks em saídas de estoque.
/// Documenta que transações explícitas com FOR UPDATE são necessárias
/// para evitar race conditions e manter quantidade positiva.
/// </summary>
public class EstoqueConcurrencyTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task RegistrarSaidaEstoque_ComDoisThreadsVendendoMesmoLote_MantemEstoquePositivo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        // ARRANGE: Setup com lote de 10 unidades
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        Guid itemEstoqueId;

        await using (var setupContext = fixture.CreateDbContext())
        {
            var categoria = new Categoria
            {
                Id = categoriaId,
                EmpresaId = empresaId,
                Nome = "Eletrônicos"
            };
            var produto = new Produto
            {
                Id = produtoId,
                EmpresaId = empresaId,
                CategoriaId = categoriaId,
                Nome = "Produto Teste",
                SkuBase = CodigoSku.From("SKU-TEST"),
                Status = StatusProduto.Ativo
            };
            var item = new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                QuantidadeAtual = Quantidade.From(10),
                QuantidadeInicial = Quantidade.From(10),
                QuantidadeMinima = 1,
                CustoUnitario = Dinheiro.FromDecimal(100m),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow
            };

            itemEstoqueId = item.Id;

            await setupContext.Set<Categoria>().AddAsync(categoria);
            await setupContext.Set<Produto>().AddAsync(produto);
            await setupContext.Set<ItemEstoque>().AddAsync(item);
            await setupContext.SaveChangesAsync();
        }

        // ACT: Executar 2 saídas simultâneas (5 unidades cada = 10 total)
        var task1 = Task.Run(async () =>
        {
            await using var context = fixture.CreateDbContext();
            var useCase = new RegistrarSaidaEstoqueUseCase(
                new ProdutoRepository(context),
                new ItemEstoqueRepository(context),
                new VendaRepository(context),
                new ItemVendaRepository(context),
                new MovimentacaoEstoqueRepository(context),
                context,
                NullLogger<RegistrarSaidaEstoqueUseCase>.Instance);

            return await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
                empresaId,
                new[] { new RegistrarSaidaEstoqueItemCommand(itemEstoqueId, 5, 500m, null) },
                DateTime.UtcNow, DateTime.UtcNow, null, null,
                NaturezaMovimentacaoEstoque.Venda, CanalVenda.MercadoLivre, null));
        });

        var task2 = Task.Run(async () =>
        {
            await using var context = fixture.CreateDbContext();
            var useCase = new RegistrarSaidaEstoqueUseCase(
                new ProdutoRepository(context),
                new ItemEstoqueRepository(context),
                new VendaRepository(context),
                new ItemVendaRepository(context),
                new MovimentacaoEstoqueRepository(context),
                context,
                NullLogger<RegistrarSaidaEstoqueUseCase>.Instance);

            return await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
                empresaId,
                new[] { new RegistrarSaidaEstoqueItemCommand(itemEstoqueId, 5, 500m, null) },
                DateTime.UtcNow, DateTime.UtcNow, null, null,
                NaturezaMovimentacaoEstoque.Venda, CanalVenda.Shopee, null));
        });

        // Aguarda ambas as transações
        await Task.WhenAll(task1, task2);

        // ASSERT
        var result1 = task1.Result;
        var result2 = task2.Result;

        result1.Itens.Should().HaveCount(1, "primeira transação deve registrar 1 item");
        result2.Itens.Should().HaveCount(1, "segunda transação deve registrar 1 item");

        result1.Itens.First().QuantidadeSaida.Should().Be(5);
        result2.Itens.First().QuantidadeSaida.Should().Be(5);

        // Verificar estado final: quantidade deve ser 0, nunca negativa
        await using (var assertContext = fixture.CreateDbContext())
        {
            var item = await assertContext.ItensEstoque
                .Where(i => i.Id == itemEstoqueId)
                .FirstAsync();

            item.QuantidadeAtual.Value.Should().Be(0,
                "quantidade final deve ser 0 (10 - 5 - 5), nunca negativa. " +
                "Isso valida que transações pessimistas (FOR UPDATE) funcionam.");
        }
    }
}
