using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// Guarda da camada 2 do ADR-0010 (#1238): toda tabela com coluna <c>EmpresaId</c> (ou <c>empresa_id</c>,
/// convenção das <c>mobile_*</c>, #2180) sai das migrations
/// com RLS ligado e forçado e com a policy <c>tenant_isolation</c>. A <c>ocorrencias</c> nasceu sem o
/// bloco <c>DO $rls$</c> e ninguém viu; este teste pega a próxima tabela que esquecer.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class CoberturaRlsTests(PostgreSqlDatabaseFixture fixture)
{
    /// <summary>
    /// Isentas por desenho, as mesmas de <c>skip_tables</c> na migration AddRowLevelSecurity. O prefixo
    /// <c>mobile_</c> saiu daqui no #2180: as tabelas Mobile com empresa têm RLS como o núcleo.
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

    /// <summary>
    /// Tabelas não-Mobile com <c>empresa_id</c> em snake_case que a guarda passou a enxergar no #2180 (antes
    /// ela só procurava <c>EmpresaId</c>). <c>report_runs</c> tem empresa nula de propósito (AdminSaaS);
    /// <c>outbox_evento_integracao</c> é lida pelo worker de todas as empresas. Cada uma pede avaliação
    /// própria; remova daqui ao corrigir e nunca acrescente tabela nova.
    /// </summary>
    private static readonly string[] SnakeCaseSemRlsAntesDe2180 = ["outbox_evento_integracao", "report_runs"];

    [SkippableFact]
    public async Task TodaTabelaComEmpresaIdTemRlsForcadoETenantIsolation()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var semRls = await db.Database.SqlQueryRaw<string>("""
            SELECT DISTINCT c.relname AS "Value"
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN information_schema.columns col
              ON col.table_schema = n.nspname AND col.table_name = c.relname
             AND col.column_name IN ('EmpresaId', 'empresa_id')
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
            .Except(IsentasPorDesenho)
            .Except(SemRlsAntesDe1238)
            .Except(SnakeCaseSemRlsAntesDe2180)
            .ToList();
        faltando.Should().BeEmpty(
            $"tabela com EmpresaId sem RLS vaza entre empresas se o filtro do EF falhar; sem RLS: {string.Join(", ", faltando)}");
    }

    /// <summary>
    /// Tabelas <c>mobile_*</c> sem coluna de empresa (#2180): filhas por FK dos lotes e pedidos. Ficam fora
    /// da policy por não terem como filtrar; tabela Mobile nova sem empresa precisa entrar aqui de propósito.
    /// As <c>mobile_push_*</c> (avisos pré-pareamento, também sem empresa) nascem só pelo SQL
    /// <c>013_AddPushReceipts.sql</c> do Mobile, fora das migrations, e não aparecem neste banco.
    /// </summary>
    private static readonly string[] MobileSemEmpresa = ["mobile_batch_items", "mobile_order_items"];

    [SkippableFact]
    public async Task TabelaMobileSemEmpresaEstaNaListaExplicita()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var semEmpresa = await db.Database.SqlQueryRaw<string>("""
            SELECT c.relname AS "Value"
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema()
              AND c.relkind = 'r'
              AND c.relname LIKE 'mobile\_%'
              AND NOT EXISTS (SELECT 1 FROM information_schema.columns col
                              WHERE col.table_schema = n.nspname AND col.table_name = c.relname
                                AND col.column_name IN ('EmpresaId', 'empresa_id'))
            ORDER BY c.relname
            """).ToListAsync();

        semEmpresa.Should().BeEquivalentTo(MobileSemEmpresa,
            "tabela mobile_ sem coluna de empresa fica sem RLS e precisa ser decisão explícita");
    }
}
