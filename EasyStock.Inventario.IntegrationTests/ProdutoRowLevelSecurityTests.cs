using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Inventario.IntegrationTests;

/// <summary>
/// Fatia 0 do modulo Inventario: prova que o harness de RLS isola de verdade, sem
/// falso-verde. Roda na tabela EXISTENTE 'produtos' (tenant-scoped) porque as
/// entidades do modulo (Contagem etc.) ainda nao existem. As queries usam
/// IgnoreQueryFilters() de proposito: removem o filtro EF de tenant para que o que
/// e testado seja a RLS do banco (nao o filtro do EF). Conecta como o role
/// NOSUPERUSER 'rls_test_client' — como superuser a RLS seria ignorada e o COUNT
/// viria 2. Espelha a mecanica de Infra.Postgre.IntegrationTests/RowLevelSecurityTests,
/// isolada no slnf do CI.
/// </summary>
[Collection("PostgresRlsCollection")]
public class ProdutoRowLevelSecurityTests(PostgresRlsFixture fixture)
{
    [SkippableFact]
    public void Harness_executa_de_verdade_quando_env_var_presente()
    {
        // Guarda anti-falso-verde: em CI a env var existe, entao este teste NAO pula —
        // se o harness estiver indisponivel (fiacao YAML->processo quebrada) os
        // SkippableFact abaixo SKIPariam e o gate ficaria verde-vazio; aqui isso vira
        // VERMELHO. Localmente sem a env var, SKIPa visivel (Skip.If em vez de no-op
        // silencioso; ADR-0023/#394).
        var envPresente = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("EASYSTOCK_IT_PG"));
        Skip.If(!envPresente, "EASYSTOCK_IT_PG ausente (rodada local sem service container).");

        fixture.IsAvailable.Should().BeTrue(
            "EASYSTOCK_IT_PG setada (CI) mas o harness Postgres esta indisponivel: "
            + (fixture.UnavailableReason ?? "(sem motivo)"));
    }

    [SkippableFact]
    public async Task Sem_set_de_tenant_query_retorna_zero_linhas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();

        // Sem SET: current_setting('app.empresa_id', true)='' -> NULLIF('','')::uuid=NULL
        // -> comparacao UNKNOWN/false -> 0 linhas (fail-closed). PROVA que o role esta
        // sujeito a policy: se conectasse como superuser, viriam 2 (canario anti-falso-verde).
        var count = await ctx.Produtos.IgnoreQueryFilters().CountAsync();
        count.Should().Be(0, "fail-closed quando o contexto de tenant nao foi setado");
    }

    [SkippableFact]
    public async Task Set_tenant_A_ve_so_dados_de_A()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var (a, _) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await SetTenantAsync(ctx, a.Id);

        var produtos = await ctx.Produtos.IgnoreQueryFilters().ToListAsync();
        produtos.Should().HaveCount(1);
        produtos[0].EmpresaId.Should().Be(a.Id);
    }

    [SkippableFact]
    public async Task Set_tenant_B_ve_so_dados_de_B()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var (_, b) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await SetTenantAsync(ctx, b.Id);

        var produtos = await ctx.Produtos.IgnoreQueryFilters().ToListAsync();
        produtos.Should().HaveCount(1);
        produtos[0].EmpresaId.Should().Be(b.Id);
    }

    [SkippableFact]
    public async Task Bypass_RLS_ve_dados_dos_dois_tenants()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var (a, b) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        var produtos = await ctx.Produtos.IgnoreQueryFilters().ToListAsync();
        produtos.Should().HaveCount(2);
        produtos.Select(p => p.EmpresaId).Should().Contain(new[] { a.Id, b.Id });
    }

    // ──────────────────────────────────────────────────────────────────────────

    private async Task<(Empresa A, Empresa B)> SeedDuasEmpresasComProdutosAsync()
    {
        await fixture.ResetDatabaseAsync();

        await using var seed = fixture.CreateRlsClientDbContext();
        // Conexao aberta: SET (sem LOCAL) vive pela sessao Npgsql; manter aberta garante
        // que o INSERT do SaveChanges rode na MESMA conexao e veja o bypass.
        await seed.Database.OpenConnectionAsync();
        // Fixture cria DbContext sem interceptor -> sem este bypass a policy zeraria o INSERT.
        await seed.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        var empresaA = new Empresa
        {
            Id = Guid.NewGuid(),
            Nome = "Empresa A",
            Documento = "11111111111",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };
        var empresaB = new Empresa
        {
            Id = Guid.NewGuid(),
            Nome = "Empresa B",
            Documento = "22222222222",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };
        var categoriaA = new Categoria
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaA.Id,
            Nome = "Geral",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };
        var categoriaB = new Categoria
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaB.Id,
            Nome = "Geral",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };

        seed.Empresas.AddRange(empresaA, empresaB);
        seed.Categorias.AddRange(categoriaA, categoriaB);
        seed.Produtos.AddRange(
            new Produto
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaA.Id,
                CategoriaId = categoriaA.Id,
                Nome = "Produto A",
                Tipo = TipoProduto.Fisico,
                SkuBase = CodigoSku.From("PROD-A"),
                Status = StatusProduto.Ativo,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow,
            },
            new Produto
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaB.Id,
                CategoriaId = categoriaB.Id,
                Nome = "Produto B",
                Tipo = TipoProduto.Fisico,
                SkuBase = CodigoSku.From("PROD-B"),
                Status = StatusProduto.Ativo,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow,
            });

        await seed.SaveChangesAsync();
        return (empresaA, empresaB);
    }

    private static async Task SetTenantAsync(EasyStockDbContext ctx, Guid tenantId) =>
        // set_config parametrizado ({0}): evita EF1002 (interpolar Guid de runtime em
        // ExecuteSqlRaw e' erro sob -warnaserror). is_local=false => SET de sessao.
        await ctx.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.empresa_id', {0}, false), set_config('app.bypass_rls', 'false', false)",
            tenantId.ToString());
}
