using EasyStock.Application.UseCases.Caixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2003 (estoque-05): <c>Quantidade</c> é numeric(18,3) e a baixa de insumo grava 0,4 kg ou 2,5 L, mas as consultas
/// convertiam com <c>(int)</c>, que o Postgres arredonda. O insumo com 0,4 kg "não tinha estoque" (e podia ser
/// removido), a saída de 0,3 kg por vez dava velocidade zero e o valor do estoque saía arredondado por lote.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class QuantidadeFracionadaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private sealed record Semente(Guid EmpresaId, Guid ProdutoId);

    [SkippableFact]
    public async Task InsumoComMeioQuilo_TemEstoque_EnaoPodeSerRemovido()
    {
        var s = await SemearAsync(saldo: 0.4m);

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        var repo = new ItemEstoqueRepository(db);

        (await repo.ExisteEstoqueDoProdutoAsync(s.EmpresaId, s.ProdutoId)).Should().BeTrue("0,4 kg é saldo");
        (await repo.GetContadoresEstoqueAsync(s.EmpresaId)).Should().Be((1, 1), "o lote de 0,4 kg tem saldo");
    }

    [SkippableFact]
    public async Task LoteVazio_ContinuaSemEstoque()
    {
        var s = await SemearAsync(saldo: 0m);

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        var repo = new ItemEstoqueRepository(db);

        (await repo.ExisteEstoqueDoProdutoAsync(s.EmpresaId, s.ProdutoId)).Should().BeFalse();
        (await repo.GetContadoresEstoqueAsync(s.EmpresaId)).Should().Be((1, 0));
    }

    [SkippableFact]
    public async Task SaidasDeTresDecimosPorVez_DaoVelocidade_ENaoZero()
    {
        var s = await SemearAsync(saldo: 10m, saidas: [0.3m, 0.3m, 0.3m]);

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        var repo = new MovimentacaoEstoqueRepository(db);
        var de = DateTime.UtcNow.AddDays(-30);
        var ate = DateTime.UtcNow.AddMinutes(1);

        (await repo.GetTaxaSaidaDiariaAsync(s.EmpresaId, s.ProdutoId, de, ate)).Should().BeApproximately(0.9m / 30m, 0.001m);
        (await repo.GetTaxaSaidaDiariaPorProdutoAsync(s.EmpresaId, [s.ProdutoId], de, ate))[s.ProdutoId]
            .Should().BeApproximately(0.9m / 30m, 0.001m);
    }

    [SkippableFact]
    public async Task ValorDoEstoque_UsaAQuantidadeFracionada_SemArredondarPorLote()
    {
        var s = await SemearAsync(saldo: 2.5m, preco: 10m, custo: 4m);

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);

        var (_, valorTotal, _) = await new ItemEstoqueRepository(db).GetResumoEstoqueAsync(s.EmpresaId);
        valorTotal.Should().Be(25m, "2,5 × R$ 10 (o (int) dava 3 × 10 = 30)");

        var painel = await new AnalyticsRepository(db, new CaixaSaldoCalculator(new CaixaRepository(db)))
            .GetDashboardResumoAsync(s.EmpresaId);
        painel.ValorTotalEstoque.Should().Be(25m);
        painel.ValorCustoEstoque.Should().Be(10m, "2,5 × R$ 4");
    }

    private async Task<Semente> SemearAsync(decimal saldo, decimal preco = 10m, decimal custo = 4m, decimal[]? saidas = null)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var agora = DateTime.UtcNow;
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var loteId = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Frac", Documento = empresaId.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
        db.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Insumos", CriadoEm = agora, AlteradoEm = agora });
        db.Produtos.Add(new Produto
        {
            Id = produtoId, EmpresaId = empresaId, CategoriaId = categoriaId, Nome = "Molho base", Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo, CriadoEm = agora, AlteradoEm = agora,
        });
        db.ItensEstoque.Add(new ItemEstoque
        {
            Id = loteId, EmpresaId = empresaId, ProdutoId = produtoId,
            QuantidadeInicial = Quantidade.From(saldo), QuantidadeAtual = Quantidade.From(saldo),
            CustoUnitario = Dinheiro.FromDecimal(custo), PrecoVendaSugerido = Dinheiro.FromDecimal(preco),
            EntradaEm = agora, Status = StatusItemEstoque.Ok, CriadoEm = agora, AlteradoEm = agora,
        });
        foreach (var quantidade in saidas ?? [])
            db.MovimentacoesEstoque.Add(new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produtoId, ItemEstoqueId = loteId,
                Tipo = TipoMovimentacaoEstoque.Saida, Natureza = NaturezaMovimentacaoEstoque.UsoInterno,
                Quantidade = Quantidade.From(quantidade), DataMovimentacao = agora.AddDays(-1), CriadoEm = agora,
            });
        await db.SaveChangesAsync();
        return new Semente(empresaId, produtoId);
    }
}
