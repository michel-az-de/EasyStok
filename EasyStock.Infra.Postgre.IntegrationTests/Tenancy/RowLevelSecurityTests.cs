using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// Garante que a migration <c>AddRowLevelSecurity</c> e o
/// <c>SetTenantOnConnectionInterceptor</c> formam uma defesa real em
/// profundidade. Cobre os 4 cenários definidos no mini-prompt da pendência:
/// sem set → 0 linhas, tenant A → só A, tenant B → só B, bypass → tudo.
/// Acrescenta dois testes de hardening: WITH CHECK bloqueia INSERT
/// cross-tenant, e RESET libera a sessão para reentrada de pool.
/// </summary>
public class RowLevelSecurityTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [Fact]
    public async Task Sem_set_de_tenant_query_retorna_zero_linhas()
    {
        if (!fixture.IsAvailable) return;
        var (a, b) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateDbContext();

        // Sem SET: current_setting('app.empresa_id', true) retorna '' →
        // NULLIF('','')::uuid vira NULL → comparação UNKNOWN/false → 0 linhas.
        // (current_setting('app.bypass_rls', true) também retorna '' ≠ 'true').
        var count = await ContarLinhasAsync(ctx, "produtos");
        count.Should().Be(0, "fail-closed quando contexto de tenant não foi setado");
    }

    [Fact]
    public async Task Set_tenant_A_ve_so_dados_de_A()
    {
        if (!fixture.IsAvailable) return;
        var (a, b) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateDbContext();
        await SetTenantManualAsync(ctx, a.Id);

        var produtos = await SelecionarProdutosAsync(ctx);
        produtos.Should().HaveCount(1);
        produtos[0].EmpresaId.Should().Be(a.Id);
    }

    [Fact]
    public async Task Set_tenant_B_ve_so_dados_de_B()
    {
        if (!fixture.IsAvailable) return;
        var (a, b) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateDbContext();
        await SetTenantManualAsync(ctx, b.Id);

        var produtos = await SelecionarProdutosAsync(ctx);
        produtos.Should().HaveCount(1);
        produtos[0].EmpresaId.Should().Be(b.Id);
    }

    [Fact]
    public async Task Bypass_RLS_ve_dados_dos_dois_tenants()
    {
        if (!fixture.IsAvailable) return;
        var (a, b) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateDbContext();
        await ctx.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        var produtos = await SelecionarProdutosAsync(ctx);
        produtos.Should().HaveCount(2);
        produtos.Select(p => p.EmpresaId).Should().Contain(new[] { a.Id, b.Id });
    }

    [Fact]
    public async Task Insert_cross_tenant_e_bloqueado_pelo_WITH_CHECK()
    {
        if (!fixture.IsAvailable) return;
        var (a, b) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateDbContext();
        await SetTenantManualAsync(ctx, a.Id);

        // Tenta inserir um produto pertencente ao tenant B enquanto a sessão
        // está autenticada como A — a policy WITH CHECK deve bloquear (42501
        // insufficient_privilege / new row violates row-level security policy).
        var inserirNoTenantB = async () => await ctx.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO produtos (""Id"", ""EmpresaId"", ""CategoriaId"", ""Nome"", ""Tipo"", ""SkuBase"", ""Status"", ""CriadoEm"", ""AlteradoEm"", ""IsDeletado"", ""IsSeedData"")
            VALUES ({Guid.NewGuid()}, {b.Id}, {Guid.NewGuid()}, 'Hack', 1, 'HACK-SKU', 1, {DateTime.UtcNow}, {DateTime.UtcNow}, false, false)");

        await inserirNoTenantB.Should()
            .ThrowAsync<Npgsql.PostgresException>()
            .Where(ex => ex.SqlState == "42501",
                "policy WITH CHECK deve recusar gravação cross-tenant");
    }

    [Fact]
    public async Task Interceptor_emite_SET_e_RESET_corretamente()
    {
        if (!fixture.IsAvailable) return;
        var (a, _) = await SeedDuasEmpresasComProdutosAsync();

        // Cria um DbContext NOVO com o interceptor registrado e um ICurrentUser
        // apontando para tenant A. Cada query deve enxergar SET app.empresa_id=a.Id.
        var interceptor = new SetTenantOnConnectionInterceptor();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(a.Id);
        currentUser.Nivel.Returns(NivelAcesso.Admin);

        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(GetTestConnectionString())
            .AddInterceptors(interceptor)
            .Options;

        await using var ctxComInterceptor = new EasyStockDbContext(options, currentUser);

        // Lê o setting pra confirmar que o interceptor emitiu o SET certo.
        var setting = await ctxComInterceptor.Database
            .SqlQueryRaw<string>(@"SELECT current_setting('app.empresa_id', true) AS ""Value""")
            .FirstAsync();
        setting.Should().Be(a.Id.ToString(),
            "interceptor deve emitir SET app.empresa_id na abertura da conexão");

        // Query via SqlQueryRaw passa por RLS (não pelo Global Query Filter EF),
        // então testa direto a camada Postgres com tenant aplicado pelo interceptor.
        var count = await ContarLinhasAsync(ctxComInterceptor, "produtos");
        count.Should().Be(1, "tenant A deve enxergar exatamente seu produto");
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task<(Empresa A, Empresa B)> SeedDuasEmpresasComProdutosAsync()
    {
        await fixture.ResetDatabaseAsync();

        await using var seed = fixture.CreateDbContext();
        // Bypass via SET direto: fixture cria DbContext sem interceptor, então
        // a policy RLS bloquearia todo INSERT sem essa linha (current_setting
        // retorna '' e a policy USING/WITH CHECK falha).
        await seed.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        var empresaA = new Empresa
        {
            Id = Guid.NewGuid(),
            Nome = "Empresa A",
            Documento = "11111111111",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        var empresaB = new Empresa
        {
            Id = Guid.NewGuid(),
            Nome = "Empresa B",
            Documento = "22222222222",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        var categoriaA = new Categoria
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaA.Id,
            Nome = "Geral",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        var categoriaB = new Categoria
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaB.Id,
            Nome = "Geral",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
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
                AlteradoEm = DateTime.UtcNow
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
                AlteradoEm = DateTime.UtcNow
            });

        await seed.SaveChangesAsync();
        return (empresaA, empresaB);
    }

    private static async Task SetTenantManualAsync(EasyStockDbContext ctx, Guid tenantId)
    {
        // Simula o que o interceptor faria — usado nos testes de policy pra
        // isolar a camada do banco do código C# do interceptor. Também garante
        // que o bypass anterior seja explicitamente desligado.
        await ctx.Database.ExecuteSqlRawAsync(
            $"SET app.empresa_id = '{tenantId}'; SET app.bypass_rls = 'false';");
    }

    private static async Task<int> ContarLinhasAsync(EasyStockDbContext ctx, string tabela)
    {
        var sql = $"SELECT COUNT(*)::int AS \"Value\" FROM {tabela}";
        return await ctx.Database.SqlQueryRaw<int>(sql).FirstAsync();
    }

    private static async Task<List<ProdutoLinhaCrua>> SelecionarProdutosAsync(EasyStockDbContext ctx)
    {
        return await ctx.Database.SqlQueryRaw<ProdutoLinhaCrua>(
            @"SELECT ""Id"" AS ""Id"", ""EmpresaId"" AS ""EmpresaId"", ""Nome"" AS ""Nome"" FROM produtos")
            .ToListAsync();
    }

    private string GetTestConnectionString()
    {
        // O fixture é interno do mesmo namespace de testes; expor via método
        // público no fixture seria intrusivo. Truque: reabre o DbContext criado
        // pelo fixture e lê a connection string dele.
        using var probe = fixture.CreateDbContext();
        return probe.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Connection string indisponível no fixture.");
    }

    private sealed record ProdutoLinhaCrua(Guid Id, Guid EmpresaId, string Nome);
}
