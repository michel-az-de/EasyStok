using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Inventario.IntegrationTests;

/// <summary>
/// Fatia 2: prova que a RLS criada na migration AddInventario isola a tabela NOVA
/// 'contagens' por tenant (mesma mecanica de ProdutoRowLevelSecurityTests). Tambem
/// exercita migrate-from-scratch da AddInventario — incl. a coluna xmin de
/// concorrencia otimista — via ResetDatabaseAsync (DROP SCHEMA + Migrate). Conecta
/// como o role NOSUPERUSER rls_test_client (RLS so vale para nao-superuser).
/// </summary>
[Collection("PostgresRlsCollection")]
public class ContagemRowLevelSecurityTests(PostgresRlsFixture fixture)
{
    [SkippableFact]
    public async Task Sem_set_de_tenant_contagens_retorna_zero()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await SeedContagensDeDoisTenantsAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();

        var count = await ctx.Contagens.IgnoreQueryFilters().CountAsync();
        count.Should().Be(0, "fail-closed quando o tenant nao foi setado na conexao");
    }

    [SkippableFact]
    public async Task Set_tenant_A_ve_so_contagem_de_A()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var (a, _) = await SeedContagensDeDoisTenantsAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await SetTenantAsync(ctx, a);

        var contagens = await ctx.Contagens.IgnoreQueryFilters().ToListAsync();
        contagens.Should().HaveCount(1);
        contagens[0].EmpresaId.Should().Be(a);
    }

    [SkippableFact]
    public async Task Bypass_ve_as_duas_contagens()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await SeedContagensDeDoisTenantsAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        var contagens = await ctx.Contagens.IgnoreQueryFilters().ToListAsync();
        contagens.Should().HaveCount(2);
    }

    private async Task<(Guid A, Guid B)> SeedContagensDeDoisTenantsAsync()
    {
        await fixture.ResetDatabaseAsync();

        var empresaAId = Guid.NewGuid();
        var empresaBId = Guid.NewGuid();

        await using var seed = fixture.CreateRlsClientDbContext();
        await seed.Database.OpenConnectionAsync();
        await seed.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        seed.Empresas.AddRange(
            NovaEmpresa(empresaAId, "Empresa A", "11111111111"),
            NovaEmpresa(empresaBId, "Empresa B", "22222222222"));
        seed.Contagens.AddRange(
            Contagem.Criar(empresaAId, EscopoContagem.Todos, null, ModoContagem.Visivel, EstrategiaLoteContagem.Guiado, Guid.NewGuid()),
            Contagem.Criar(empresaBId, EscopoContagem.Todos, null, ModoContagem.Visivel, EstrategiaLoteContagem.Guiado, Guid.NewGuid()));

        await seed.SaveChangesAsync();
        return (empresaAId, empresaBId);
    }

    private static Empresa NovaEmpresa(Guid id, string nome, string documento) =>
        new()
        {
            Id = id,
            Nome = nome,
            Documento = documento,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };

    private static async Task SetTenantAsync(EasyStockDbContext ctx, Guid tenantId) =>
        await ctx.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.empresa_id', {0}, false), set_config('app.bypass_rls', 'false', false)",
            tenantId.ToString());
}
