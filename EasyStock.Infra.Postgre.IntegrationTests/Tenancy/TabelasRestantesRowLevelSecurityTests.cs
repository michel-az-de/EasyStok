using EasyStock.Domain.Entities;
using EasyStock.Domain.Integration;
using EasyStock.Domain.Reporting;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// Camada 2 do ADR-0010 nas tabelas com empresa ou loja que tinham ficado sem RLS (#3108). Sete têm coluna de
/// empresa e recebem a policy do núcleo; <c>configuracoes_loja</c> só tem <c>LojaId</c> e segue a loja. Roda como
/// <c>rls_test_client</c> (NOSUPERUSER, NOBYPASSRLS): o superusuário ignora RLS e não provaria nada.
/// </summary>
public sealed class TabelasRestantesRowLevelSecurityTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    /// <summary>Tabela e coluna de empresa; <c>null</c> quando o isolamento vem da loja.</summary>
    private static readonly (string Tabela, string? Coluna)[] ComPolicy =
    [
        ("cliente_alteracoes", "\"EmpresaId\""), ("entity_alteracoes", "\"EmpresaId\""),
        ("etiqueta_templates", "\"EmpresaId\""), ("etiqueta_empresa_default", "\"EmpresaId\""),
        ("preferencias_menu_usuario", "\"EmpresaId\""), ("outbox_evento_integracao", "empresa_id"),
        ("report_runs", "empresa_id"), ("configuracoes_loja", null),
    ];

    [SkippableFact]
    public async Task As_oito_tabelas_tem_RLS_forcada_e_tenant_isolation()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        await using var db = fixture.CreateDbContext();

        // FORCE só obriga o dono (em produção, a API e o Worker); o login de teste não é dono, então vale o catálogo.
        var protegidas = await db.Database.SqlQueryRaw<string>("""
            SELECT c.relname AS "Value"
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relkind = 'r'
              AND c.relrowsecurity AND c.relforcerowsecurity
              AND EXISTS (SELECT 1 FROM pg_policies p
                          WHERE p.schemaname = n.nspname AND p.tablename = c.relname AND p.policyname = 'tenant_isolation')
            """).ToListAsync();

        protegidas.Should().Contain(ComPolicy.Select(t => t.Tabela));
    }

    [SkippableFact]
    public async Task Tenant_A_ve_so_as_linhas_de_A()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var s = await SemearDuasEmpresasAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await DefinirTenantAsync(ctx, s.A);

        foreach (var (tabela, coluna) in ComPolicy)
        {
            var sql = $"SELECT DISTINCT {coluna ?? "\"LojaId\""} AS \"Value\" FROM {tabela}";
            var donos = await ctx.Database.SqlQueryRaw<Guid>(sql).ToListAsync();
            donos.Should().Equal([coluna is null ? s.LojaA : s.A], $"{tabela} não pode devolver linha de outra empresa");
        }
    }

    [SkippableFact]
    public async Task Sem_tenant_devolvem_zero_linhas_e_o_bypass_ve_as_duas_empresas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        await SemearDuasEmpresasAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        foreach (var (tabela, _) in ComPolicy)
            (await ContarAsync(ctx, tabela)).Should().Be(0, $"{tabela} é fail-closed sem app.empresa_id");

        await ctx.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");
        foreach (var (tabela, _) in ComPolicy)
            (await ContarAsync(ctx, tabela)).Should().Be(2, $"o bypass da allowlist lê {tabela} das duas empresas");
    }

    [SkippableFact]
    public async Task Insert_em_outra_empresa_e_bloqueado_pelo_WITH_CHECK()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var s = await SemearDuasEmpresasAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await DefinirTenantAsync(ctx, s.A);

        var agora = DateTime.UtcNow;
        var pelaEmpresa = async () => await ctx.Database.ExecuteSqlAsync($"""
            INSERT INTO etiqueta_templates ("Id", "EmpresaId", "Nome", "LayoutJson", "IsDefault", "CriadoEm", "AlteradoEm")
            VALUES ({Guid.NewGuid()}, {s.B}, {"Invasor"}, {"{}"}, {false}, {agora}, {agora})
            """);
        await pelaEmpresa.Should().ThrowAsync<Npgsql.PostgresException>().Where(ex => ex.SqlState == "42501");

        // Loja de B ainda sem configuração: o WITH CHECK recusa pela loja, não pela chave única.
        var pelaLoja = async () => await ctx.Database.ExecuteSqlAsync($"""
            INSERT INTO configuracoes_loja ("Id", "LojaId", "GerarContaReceberAutomaticaDePedido",
                "GerarContaPagarAutomaticaDePedidoFornecedor", "StatusPedidoQueGeraContaReceber", "CriadoEm", "AlteradoEm")
            VALUES ({Guid.NewGuid()}, {s.LojaBSemConfiguracao}, {false}, {false}, {"entregue"}, {agora}, {agora})
            """);
        await pelaLoja.Should().ThrowAsync<Npgsql.PostgresException>().Where(ex => ex.SqlState == "42501");
    }

    /// <summary>
    /// <c>report_runs.empresa_id</c> é nulo de propósito no contexto AdminSaaS (CHECK da tabela). A policy não
    /// casa NULL: a run de plataforma só aparece sob bypass, nunca para uma loja.
    /// </summary>
    [SkippableFact]
    public async Task Run_de_plataforma_sem_empresa_so_aparece_sob_bypass()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var s = await SemearDuasEmpresasAsync();
        var admin = ReportRun.Enqueue(Guid.Empty, Guid.NewGuid(), "plataforma.teste", ReportCategoria.Vendas,
            ReportContexto.AdminSaaS, "{}", "hash-admin", ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7),
            motivoExecucao: "teste de RLS");
        await using (var seed = fixture.CreateDbContext())
        {
            seed.ReportRuns.Add(admin);
            await seed.SaveChangesAsync();
        }

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await DefinirTenantAsync(ctx, s.A);
        (await ctx.ReportRuns.AsNoTracking().AnyAsync(r => r.Id == admin.Id)).Should().BeFalse();

        await ctx.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");
        (await ctx.ReportRuns.AsNoTracking().AnyAsync(r => r.Id == admin.Id)).Should().BeTrue();
    }

    [SkippableFact]
    public async Task Down_da_migracao_desliga_a_RLS_das_oito_tabelas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        await fixture.ResetDatabaseAsync();
        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        var migracoes = db.Database.GetMigrations().ToList();
        var anterior = migracoes[migracoes.FindIndex(m => m.EndsWith("_AddRlsTabelasRestantes", StringComparison.Ordinal)) - 1];

        (await ContarComRlsAsync(db)).Should().Be(ComPolicy.Length, "o Up liga a policy nas oito");

        try
        {
            await migrator.MigrateAsync(anterior);
            (await ContarComRlsAsync(db)).Should().Be(0, "o Down é o rollback e desliga RLS e policy");
        }
        finally
        {
            await fixture.ResetDatabaseAsync();
        }
    }

    private static Task<int> ContarComRlsAsync(DbContext db)
    {
        var lista = string.Join(", ", ComPolicy.Select(t => $"'{t.Tabela}'"));
        var sql = $"""
            SELECT COUNT(*)::int AS "Value"
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relkind = 'r' AND c.relname IN ({lista})
              AND (c.relrowsecurity OR c.relforcerowsecurity
                   OR EXISTS (SELECT 1 FROM pg_policies p
                              WHERE p.schemaname = n.nspname AND p.tablename = c.relname AND p.policyname = 'tenant_isolation'))
            """;
        return db.Database.SqlQueryRaw<int>(sql).FirstAsync();
    }

    private static Task<int> ContarAsync(DbContext ctx, string tabela)
    {
        var sql = $"SELECT COUNT(*)::int AS \"Value\" FROM {tabela}";
        return ctx.Database.SqlQueryRaw<int>(sql).FirstAsync();
    }

    /// <summary>Simula o interceptor: <c>set_config</c> de sessão, como o <c>SET</c> sem LOCAL.</summary>
    private static Task DefinirTenantAsync(DbContext ctx, Guid empresa) =>
        ctx.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresa.ToString()}, false)");

    private sealed record Semeadura(Guid A, Guid B, Guid LojaA, Guid LojaBSemConfiguracao);

    /// <summary>Uma linha por empresa em cada uma das oito tabelas, semeada como superusuário.</summary>
    private async Task<Semeadura> SemearDuasEmpresasAsync()
    {
        await fixture.ResetDatabaseAsync();
        await using var seed = fixture.CreateDbContext();
        var a = Empresa.Criar("RLS restantes A", null);
        var b = Empresa.Criar("RLS restantes B", null);
        seed.Empresas.AddRange(a, b);
        var lojas = new Dictionary<Guid, Guid> { [a.Id] = Guid.NewGuid(), [b.Id] = Guid.NewGuid() };
        var lojaBSemConfiguracao = Guid.NewGuid();

        foreach (var empresa in new[] { a.Id, b.Id })
        {
            seed.Set<EntityAlteracao>().Add(new EntityAlteracao
            {
                Id = Guid.NewGuid(), EmpresaId = empresa, TipoEntidade = "Teste3108", EntidadeId = Guid.NewGuid(),
                Acao = "Update", AlteradoEm = DateTime.UtcNow,
            });
            seed.Set<OutboxEventoIntegracao>().Add(
                OutboxEventoIntegracao.Criar(empresa, "teste.3108", "Pedido", Guid.NewGuid(), "{}"));
            seed.ReportRuns.Add(ReportRun.Enqueue(empresa, Guid.NewGuid(), "vendas.por-periodo", ReportCategoria.Vendas,
                ReportContexto.Tenant, "{}", Guid.NewGuid().ToString("N"), ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7)));
        }
        await seed.SaveChangesAsync();

        // As demais dependem de cliente, usuário e loja, que não interessam aqui: semeia a linha crua sem os
        // gatilhos de FK (só o superusuário pode), na mesma conexão.
        await seed.Database.OpenConnectionAsync();
        await seed.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        var agora = DateTime.UtcNow;
        foreach (var empresa in new[] { a.Id, b.Id })
        {
            var loja = lojas[empresa];
            var template = Guid.NewGuid();
            await seed.Database.ExecuteSqlAsync($"""
                INSERT INTO lojas ("Id", "EmpresaId", "Nome", "Ativa", "CriadoEm", "AlteradoEm")
                VALUES ({loja}, {empresa}, {"Loja 3108"}, {true}, {agora}, {agora})
                """);
            await seed.Database.ExecuteSqlAsync($"""
                INSERT INTO configuracoes_loja ("Id", "LojaId", "GerarContaReceberAutomaticaDePedido",
                    "GerarContaPagarAutomaticaDePedidoFornecedor", "StatusPedidoQueGeraContaReceber", "CriadoEm", "AlteradoEm")
                VALUES ({Guid.NewGuid()}, {loja}, {false}, {false}, {"entregue"}, {agora}, {agora})
                """);
            await seed.Database.ExecuteSqlAsync($"""
                INSERT INTO cliente_alteracoes ("Id", "EmpresaId", "ClienteId", "Campo", "AlteradoEm")
                VALUES ({Guid.NewGuid()}, {empresa}, {Guid.NewGuid()}, {"Nome"}, {agora})
                """);
            await seed.Database.ExecuteSqlAsync($"""
                INSERT INTO preferencias_menu_usuario ("Id", "EmpresaId", "LojaId", "UsuarioId", favoritos_menu, "AtualizadaEm")
                VALUES ({Guid.NewGuid()}, {empresa}, {loja}, {Guid.NewGuid()}, {"[]"}::jsonb, {agora})
                """);
            await seed.Database.ExecuteSqlAsync($"""
                INSERT INTO etiqueta_templates ("Id", "EmpresaId", "Nome", "LayoutJson", "IsDefault", "CriadoEm", "AlteradoEm")
                VALUES ({template}, {empresa}, {"Modelo 3108"}, {"{}"}, {false}, {agora}, {agora})
                """);
            await seed.Database.ExecuteSqlAsync($"""
                INSERT INTO etiqueta_empresa_default ("EmpresaId", "TemplateId", "TemplateOrigem", "AlteradoEm")
                VALUES ({empresa}, {template}, {"E"}, {agora})
                """);
        }
        await seed.Database.ExecuteSqlAsync($"""
            INSERT INTO lojas ("Id", "EmpresaId", "Nome", "Ativa", "CriadoEm", "AlteradoEm")
            VALUES ({lojaBSemConfiguracao}, {b.Id}, {"Loja 3108 sem configuração"}, {true}, {agora}, {agora})
            """);
        await seed.Database.ExecuteSqlRawAsync("SET session_replication_role = DEFAULT");

        return new Semeadura(a.Id, b.Id, lojas[a.Id], lojaBSemConfiguracao);
    }
}
