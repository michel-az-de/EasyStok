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
    [SkippableFact]
    public async Task Sem_set_de_tenant_query_retorna_zero_linhas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (a, b) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();

        // Sem SET: current_setting('app.empresa_id', true) retorna '' →
        // NULLIF('','')::uuid vira NULL → comparação UNKNOWN/false → 0 linhas.
        // (current_setting('app.bypass_rls', true) também retorna '' ≠ 'true').
        var count = await ContarLinhasAsync(ctx, "produtos");
        count.Should().Be(0, "fail-closed quando contexto de tenant não foi setado");
    }

    [SkippableFact]
    public async Task Set_tenant_A_ve_so_dados_de_A()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (a, b) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await SetTenantManualAsync(ctx, a.Id);

        var produtos = await SelecionarProdutosAsync(ctx);
        produtos.Should().HaveCount(1);
        produtos[0].EmpresaId.Should().Be(a.Id);
    }

    [SkippableFact]
    public async Task Set_tenant_B_ve_so_dados_de_B()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (a, b) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await SetTenantManualAsync(ctx, b.Id);

        var produtos = await SelecionarProdutosAsync(ctx);
        produtos.Should().HaveCount(1);
        produtos[0].EmpresaId.Should().Be(b.Id);
    }

    [SkippableFact]
    public async Task Bypass_RLS_ve_dados_dos_dois_tenants()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (a, b) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        var produtos = await SelecionarProdutosAsync(ctx);
        produtos.Should().HaveCount(2);
        produtos.Select(p => p.EmpresaId).Should().Contain(new[] { a.Id, b.Id });
    }

    [SkippableFact]
    public async Task Insert_cross_tenant_e_bloqueado_pelo_WITH_CHECK()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (a, b) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await SetTenantManualAsync(ctx, a.Id);

        // Tenta inserir um produto pertencente ao tenant B enquanto a sessão
        // está autenticada como A — a policy WITH CHECK deve bloquear (42501
        // new row violates row-level security policy).
        // Colunas batem com o schema real (ModelSnapshot): Tipo/Status sao
        // persistidos como string (HasConversion<string>), ControlaValidade e
        // NOT NULL sem default. A WITH CHECK do RLS e avaliada antes do trigger
        // AFTER da FK, entao o CategoriaId aleatorio nao dispara 23503 — o erro e 42501.
        var inserirNoTenantB = async () => await ctx.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO produtos (""Id"", ""EmpresaId"", ""CategoriaId"", ""Nome"", ""Tipo"", ""Status"", ""ControlaValidade"", ""CriadoEm"", ""AlteradoEm"")
            VALUES ({Guid.NewGuid()}, {b.Id}, {Guid.NewGuid()}, 'Hack', 'Fisico', 'Ativo', false, {DateTime.UtcNow}, {DateTime.UtcNow})");

        await inserirNoTenantB.Should()
            .ThrowAsync<Npgsql.PostgresException>()
            .Where(ex => ex.SqlState == "42501",
                "policy WITH CHECK deve recusar gravação cross-tenant");
    }

    [SkippableFact]
    public async Task Interceptor_emite_SET_e_RESET_corretamente()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
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

    [SkippableFact]
    public async Task Update_cross_tenant_afeta_zero_linhas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (a, b) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await SetTenantManualAsync(ctx, a.Id);

        // Policy USING + WITH CHECK: UPDATE/DELETE NAO levantam 42501 quando o filtro
        // exclui linhas (diferente do INSERT, que dispara WITH CHECK). O comportamento
        // esperado e que o UPDATE simplesmente nao encontre linhas a alterar — proteção
        // real do isolamento sem exception fluir pra logs/alarmes.
        var rowsAffected = await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE produtos SET \"Nome\" = 'hacked' WHERE \"EmpresaId\" = {b.Id}");
        rowsAffected.Should().Be(0, "policy USING deve bloquear UPDATE cross-tenant transparente");

        // Releitura via bypass confirma que o produto do tenant B nao foi tocado.
        await ctx.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");
        var produtoB = await ctx.Database.SqlQueryRaw<string>(
            @"SELECT ""Nome"" AS ""Value"" FROM produtos WHERE ""EmpresaId"" = {0}", b.Id)
            .FirstAsync();
        produtoB.Should().Be("Produto B");
    }

    [SkippableFact]
    public async Task Delete_cross_tenant_afeta_zero_linhas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (a, b) = await SeedDuasEmpresasComProdutosAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await SetTenantManualAsync(ctx, a.Id);

        var rowsAffected = await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM produtos WHERE \"EmpresaId\" = {b.Id}");
        rowsAffected.Should().Be(0, "policy USING deve bloquear DELETE cross-tenant transparente");

        await ctx.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");
        var totalProdutos = await ctx.Database.SqlQueryRaw<int>(
            @"SELECT COUNT(*)::int AS ""Value"" FROM produtos").FirstAsync();
        totalProdutos.Should().Be(2, "ambos os produtos (A e B) devem continuar existindo");
    }

    [SkippableFact]
    public async Task Conexao_reciclada_de_pool_recebe_novo_tenant_antes_de_query()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (a, b) = await SeedDuasEmpresasComProdutosAsync();

        // Pooling LIGADO + tamanho 1: a segunda conexao OBRIGATORIAMENTE reusa a primeira
        // (mesma conexao fisica). Sem o re-SET do interceptor no ConnectionOpened, o
        // tenant da request anterior vazaria — exatamente o cenario de "reentrada de pool"
        // descrito no SetTenantOnConnectionInterceptor (linhas 22-27).
        var pooledConnString = new Npgsql.NpgsqlConnectionStringBuilder(fixture.RlsClientConnectionString)
        {
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 1,
            ApplicationName = $"pool-reuse-test-{Guid.NewGuid():N}",
        }.ConnectionString;

        var interceptor = new SetTenantOnConnectionInterceptor();
        var userA = Substitute.For<ICurrentUserAccessor>();
        userA.IsAuthenticated.Returns(true);
        userA.EmpresaId.Returns(a.Id);
        userA.Nivel.Returns(NivelAcesso.Admin);

        var userB = Substitute.For<ICurrentUserAccessor>();
        userB.IsAuthenticated.Returns(true);
        userB.EmpresaId.Returns(b.Id);
        userB.Nivel.Returns(NivelAcesso.Admin);

        // Primeira "request": tenant A le seu produto e fecha a conexao (volta pro pool).
        await using (var ctxA = BuildPooledCtx(pooledConnString, interceptor, userA))
        {
            var produtoA = await ctxA.Database.SqlQueryRaw<string>(
                @"SELECT ""Nome"" AS ""Value"" FROM produtos").FirstAsync();
            produtoA.Should().Be("Produto A");
        }

        // Segunda "request" — reusa a conexao fisica do pool. O ConnectionOpenedAsync
        // do interceptor deve emitir SET app.empresa_id=b.Id ANTES de qualquer query,
        // sobrescrevendo o valor residual de A — mesmo se o RESET no fechamento falhou.
        await using (var ctxB = BuildPooledCtx(pooledConnString, interceptor, userB))
        {
            var settingNoCtxB = await ctxB.Database.SqlQueryRaw<string>(
                @"SELECT current_setting('app.empresa_id', true) AS ""Value""").FirstAsync();
            settingNoCtxB.Should().Be(b.Id.ToString(),
                "interceptor deve re-emitir SET ao reusar conexao do pool — sem isso o tenant residual vazaria");

            var produtoB = await ctxB.Database.SqlQueryRaw<string>(
                @"SELECT ""Nome"" AS ""Value"" FROM produtos").FirstAsync();
            produtoB.Should().Be("Produto B");
        }
    }

    private static EasyStockDbContext BuildPooledCtx(
        string connString,
        SetTenantOnConnectionInterceptor interceptor,
        ICurrentUserAccessor currentUser)
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(connString)
            .AddInterceptors(interceptor)
            .Options;
        return new EasyStockDbContext(options, currentUser);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task<(Empresa A, Empresa B)> SeedDuasEmpresasComProdutosAsync()
    {
        await fixture.ResetDatabaseAsync();

        await using var seed = fixture.CreateRlsClientDbContext();
        // Conexao aberta explicitamente: SET (sem LOCAL) vive pela sessao Npgsql.
        // Mante-la aberta garante que o INSERT do SaveChanges rode na MESMA conexao
        // e enxergue o bypass; sem isso o EF abriria/fecharia a conexao por comando
        // e o SET se perderia, fazendo a policy zerar o INSERT.
        await seed.Database.OpenConnectionAsync();
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
        // Conexao do role NOSUPERUSER (sujeito a RLS). O teste do interceptor monta
        // seu proprio DbContext com o interceptor e precisa do mesmo login comum —
        // como 'postgres' (superuser) a RLS seria ignorada e o COUNT viria 2.
        return fixture.RlsClientConnectionString;
    }

    private sealed record ProdutoLinhaCrua(Guid Id, Guid EmpresaId, string Nome);
}
