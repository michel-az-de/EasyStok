using EasyStock.Application.UseCases.Caixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Inventario.IntegrationTests;

/// <summary>
/// Integração da fonte única de reposição (ADR-0039 / issue 748). Confirma que a projeção EF
/// <c>GetSnapshotReposicaoAsync</c> TRADUZ contra Postgres real e respeita o desenho:
/// parte de Produtos (LEFT JOIN lógico) — nunca-estocado e só-vencido vêm com
/// <c>QuantidadeVigente == 0</c>; lote vigente soma; venda (Natureza==Venda) alimenta a
/// velocidade. Roda no slnf do CI (service container); local sem Docker, SKIPa visível.
/// Tenant: <c>SetMobileTenantContext</c> libera o global query filter do EF e
/// <c>SET app.empresa_id</c> libera a RLS do Postgres (mesma sessão/conexão aberta).
/// </summary>
[Collection("PostgresRlsCollection")]
public class SnapshotReposicaoIntegrationTests(PostgresRlsFixture fixture)
{
    [SkippableFact]
    public async Task GetSnapshotReposicaoAsync_vigente_vencido_e_nunca_estocado()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var (empresaId, vigenteId, vencidoId, nuncaId) = await SeedAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        ctx.SetMobileTenantContext(empresaId);   // global query filter do EF
        await ctx.Database.OpenConnectionAsync();
        await SetTenantAsync(ctx, empresaId);     // RLS do Postgres na mesma sessão

        var repo = new AnalyticsRepository(ctx, new CaixaSaldoCalculator(new CaixaRepository(ctx)));

        var snapshot = await repo.GetSnapshotReposicaoAsync(
            empresaId, lojaId: null, diasHistorico: 30, leadTimePadraoDias: 7,
            configMinima: 5, configCritica: 2);

        snapshot.Should().HaveCount(3, "os 3 produtos ativos entram (LEFT JOIN partindo de Produto)");

        var vigente = snapshot.Single(s => s.ProdutoId == vigenteId);
        vigente.QuantidadeVigente.Should().Be(100m);
        vigente.VelocidadeMediaDia.Should().BeGreaterThan(0m, "teve venda nos ultimos 30 dias");

        var vencido = snapshot.Single(s => s.ProdutoId == vencidoId);
        vencido.QuantidadeVigente.Should().Be(0m, "lote vencido nao conta como vigente");

        var nunca = snapshot.Single(s => s.ProdutoId == nuncaId);
        nunca.QuantidadeVigente.Should().Be(0m, "produto nunca estocado entra com vigente 0 (ESGOTADO)");
        nunca.VelocidadeMediaDia.Should().Be(0m);
    }

    private async Task<(Guid EmpresaId, Guid VigenteId, Guid VencidoId, Guid NuncaId)> SeedAsync()
    {
        await fixture.ResetDatabaseAsync();
        var empresaId = Guid.NewGuid();
        var hoje = DateTime.UtcNow.Date;

        await using var seed = fixture.CreateRlsClientDbContext();
        await seed.Database.OpenConnectionAsync();
        await seed.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        var empresa = new Empresa { Id = empresaId, Nome = "Empresa Reposicao", Documento = "44444444444", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Geral", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var fornecedor = new Fornecedor { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Fornecedor", LeadTimeEstimadoDias = 4, Ativo = true, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };

        var prodVigente = NovoProduto(empresaId, categoria.Id, "Com lote vigente");
        var prodVencido = NovoProduto(empresaId, categoria.Id, "So lote vencido");
        var prodNunca = NovoProduto(empresaId, categoria.Id, "Nunca estocado");

        seed.Empresas.Add(empresa);
        seed.Categorias.Add(categoria);
        seed.Fornecedores.Add(fornecedor);
        seed.Produtos.AddRange(prodVigente, prodVencido, prodNunca);

        var itemVigente = ItemEstoque.CriarParaEntrada(
            Guid.NewGuid(), empresaId, prodVigente, null,
            Quantidade.From(100m), Dinheiro.FromDecimal(10m), null,
            hoje.AddDays(-10), "LOTE-VIG", CodigoLote.From("LV-1"),
            null, null, null, null, null, null, null,
            Validade.From(hoje.AddDays(30)), null, DateTime.UtcNow);
        itemVigente.FornecedorId = fornecedor.Id;

        var itemVencido = ItemEstoque.CriarParaEntrada(
            Guid.NewGuid(), empresaId, prodVencido, null,
            Quantidade.From(50m), Dinheiro.FromDecimal(5m), null,
            hoje.AddDays(-40), "LOTE-VEN", CodigoLote.From("LV-2"),
            null, null, null, null, null, null, null,
            Validade.From(hoje.AddDays(-5)), null, DateTime.UtcNow);

        seed.ItensEstoque.AddRange(itemVigente, itemVencido);

        seed.MovimentacoesEstoque.Add(new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ItemEstoqueId = itemVigente.Id,
            ProdutoId = prodVigente.Id,
            Tipo = TipoMovimentacaoEstoque.Saida,
            Natureza = NaturezaMovimentacaoEstoque.Venda,
            Quantidade = Quantidade.From(10m),
            ValorUnitario = Dinheiro.FromDecimal(20m),
            DataMovimentacao = hoje.AddDays(-5),
            CriadoEm = DateTime.UtcNow
        });

        await seed.SaveChangesAsync();
        return (empresaId, prodVigente.Id, prodVencido.Id, prodNunca.Id);
    }

    private static Produto NovoProduto(Guid empresaId, Guid categoriaId, string nome) => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = empresaId,
        CategoriaId = categoriaId,
        Nome = nome,
        Tipo = TipoProduto.Fisico,
        SkuBase = CodigoSku.From(nome.Replace(" ", "-")),
        Status = StatusProduto.Ativo,
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow
    };

    private static async Task SetTenantAsync(EasyStockDbContext ctx, Guid tenantId) =>
        await ctx.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.empresa_id', {0}, false), set_config('app.bypass_rls', 'false', false)",
            tenantId.ToString());
}
