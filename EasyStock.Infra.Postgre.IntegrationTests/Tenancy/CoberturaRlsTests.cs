using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// Guarda da camada 2 do ADR-0010 (#1238): toda tabela com coluna <c>EmpresaId</c> sai das migrations
/// com RLS ligado e forçado e com a policy <c>tenant_isolation</c>. A <c>ocorrencias</c> nasceu sem o
/// bloco <c>DO $rls$</c> e ninguém viu; este teste pega a próxima tabela que esquecer.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class CoberturaRlsTests(PostgreSqlDatabaseFixture fixture)
{
    /// <summary>
    /// Isentas por desenho, as mesmas da migration AddRowLevelSecurity: <c>skip_tables</c> e o prefixo
    /// <c>mobile_</c> (isolamento por loja, não por empresa).
    /// </summary>
    private static readonly string[] IsentasPorDesenho = ["admin_impersonation_logs", "TenantFeatureFlags", "fatura_contador"];

    /// <summary>
    /// Dívida anterior a esta fatia, medida em 30/09/2026 (#1238) e fora do escopo dela: cada tabela pede
    /// avaliação própria antes de ligar RLS (linha global, leitura sem tenant). Remova daqui ao corrigir;
    /// tabela nova nunca entra nesta lista.
    /// </summary>
    private static readonly string[] SemRlsAntesDe1238 =
    [
        "cliente_alteracoes", "entity_alteracoes", "etiqueta_empresa_default", "etiqueta_templates",
        "preferencias_menu_usuario",
    ];

    [SkippableFact]
    public async Task TodaTabelaComEmpresaIdTemRlsForcadoETenantIsolation()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var semRls = await db.Database.SqlQueryRaw<string>("""
            SELECT c.relname AS "Value"
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN information_schema.columns col
              ON col.table_schema = n.nspname AND col.table_name = c.relname AND col.column_name = 'EmpresaId'
            WHERE n.nspname = current_schema()
              AND c.relkind = 'r'
              AND (NOT c.relrowsecurity
                   OR NOT c.relforcerowsecurity
                   OR NOT EXISTS (SELECT 1 FROM pg_policies p
                                  WHERE p.schemaname = n.nspname AND p.tablename = c.relname
                                    AND p.policyname = 'tenant_isolation'))
            ORDER BY c.relname
            """).ToListAsync();

        var faltando = semRls
            .Where(t => !t.StartsWith("mobile_", StringComparison.Ordinal))
            .Except(IsentasPorDesenho)
            .Except(SemRlsAntesDe1238)
            .ToList();
        faltando.Should().BeEmpty(
            $"tabela com EmpresaId sem RLS vaza entre empresas se o filtro do EF falhar; sem RLS: {string.Join(", ", faltando)}");
    }
}
