using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Isolamento entre tenants das feature flags (ADR-0048), contra Postgres REAL.
///
/// <para>
/// Este teste existe porque <c>TenantFeatureFlag</c> é isenta das DUAS redes de proteção que
/// normalmente pegariam um filtro esquecido: o global query filter do EF
/// (<c>EasyStockDbContext.SkipTenantFilter</c>) e as políticas de RLS do Postgres (a tabela
/// está no <c>skip_tables</c> da migration <c>AddRowLevelSecurity</c>). Só o
/// <c>.Where(f =&gt; f.EmpresaId == empresaId)</c> escrito à mão separa um tenant do outro — e é
/// exatamente esse tipo de linha que um refactor apaga sem querer.
/// </para>
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class TenantFeatureFlagRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task Listar_nao_devolve_flag_de_outro_tenant()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();
        var repo = new TenantFeatureFlagRepository(db);

        var casaDaBaba = Guid.NewGuid();
        var fma = Guid.NewGuid();

        await repo.DefinirAsync(fma, "modulo.comercial", ativo: true, "admin@easystok.com");
        await repo.DefinirAsync(casaDaBaba, "modulo.cozinha", ativo: true, "admin@easystok.com");

        var daCasa = await repo.ListarPorEmpresaAsync(casaDaBaba);
        var daFma = await repo.ListarPorEmpresaAsync(fma);

        daCasa.Select(f => f.Feature).Should().Equal("modulo.cozinha");
        daFma.Select(f => f.Feature).Should().Equal("modulo.comercial");
    }

    [SkippableFact]
    public async Task Ativas_nao_devolve_flag_de_outro_tenant_nem_as_desligadas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();
        var repo = new TenantFeatureFlagRepository(db);

        var casaDaBaba = Guid.NewGuid();
        var fma = Guid.NewGuid();

        await repo.DefinirAsync(fma, "modulo.comercial", ativo: true, "admin@easystok.com");
        await repo.DefinirAsync(fma, "modulo.crm", ativo: false, "admin@easystok.com");
        await repo.DefinirAsync(casaDaBaba, "modulo.comercial", ativo: true, "admin@easystok.com");

        var ativasDaFma = await repo.ListarAtivasAsync(fma);

        ativasDaFma.Should().ContainSingle(because: "crm está desligada para a FMA")
            .Which.Should().Be("modulo.comercial");
    }

    [SkippableFact]
    public async Task Mesma_feature_em_tenants_diferentes_sao_linhas_independentes()
    {
        // O índice único é (EmpresaId, Feature): desligar na Casa da Babá não pode desligar
        // na FMA. Sem o EmpresaId no lookup do upsert, uma sobrescreveria a outra.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();
        var repo = new TenantFeatureFlagRepository(db);

        var casaDaBaba = Guid.NewGuid();
        var fma = Guid.NewGuid();

        await repo.DefinirAsync(fma, "modulo.comercial", ativo: true, "admin@easystok.com");
        await repo.DefinirAsync(casaDaBaba, "modulo.comercial", ativo: true, "admin@easystok.com");

        await repo.DefinirAsync(casaDaBaba, "modulo.comercial", ativo: false, "admin@easystok.com");

        (await repo.ListarAtivasAsync(fma)).Should().Equal("modulo.comercial");
        (await repo.ListarAtivasAsync(casaDaBaba)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Definir_duas_vezes_atualiza_a_mesma_linha_e_registra_quem_alterou()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();
        var repo = new TenantFeatureFlagRepository(db);

        var empresa = Guid.NewGuid();

        await repo.DefinirAsync(empresa, "modulo.crm", ativo: true, "primeiro@easystok.com");
        var depois = await repo.DefinirAsync(empresa, "modulo.crm", ativo: false, "segundo@easystok.com");

        var todas = await repo.ListarPorEmpresaAsync(empresa);
        todas.Should().ContainSingle(because: "é upsert, não insert duplicado");
        depois.Ativo.Should().BeFalse();
        depois.AlteradoPor.Should().Be("segundo@easystok.com");
    }

    [SkippableFact]
    public async Task Nome_da_feature_e_normalizado_para_nao_criar_linha_duplicada()
    {
        // "Modulo.CRM" e "modulo.crm" seriam duas linhas para o índice único (ele compara o
        // texto como veio) — e a leitura, que busca minúsculo, ignoraria uma delas.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();
        var repo = new TenantFeatureFlagRepository(db);

        var empresa = Guid.NewGuid();

        await repo.DefinirAsync(empresa, "modulo.crm", ativo: true, "admin@easystok.com");
        await repo.DefinirAsync(empresa, "  Modulo.CRM  ", ativo: false, "admin@easystok.com");

        var todas = await repo.ListarPorEmpresaAsync(empresa);
        todas.Should().ContainSingle();
        todas[0].Feature.Should().Be("modulo.crm");
        todas[0].Ativo.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Empresa_vazia_devolve_lista_vazia_em_vez_de_varrer_a_tabela()
    {
        // Guid.Empty chegando aqui significa claim ausente. Sem o guard, o filtro
        // `EmpresaId == Guid.Empty` não casaria com nada hoje — mas o dia em que alguém
        // gravar uma linha com Guid.Empty, ela viraria uma flag global acidental.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();
        var repo = new TenantFeatureFlagRepository(db);

        await repo.DefinirAsync(Guid.NewGuid(), "modulo.crm", ativo: true, "admin@easystok.com");

        (await repo.ListarPorEmpresaAsync(Guid.Empty)).Should().BeEmpty();
        (await repo.ListarAtivasAsync(Guid.Empty)).Should().BeEmpty();
    }
}
