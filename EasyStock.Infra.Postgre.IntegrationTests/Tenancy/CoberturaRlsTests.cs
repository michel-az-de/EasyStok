using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// Guarda da camada 2 do ADR-0010 (#1238): toda tabela com coluna <c>EmpresaId</c> (ou <c>empresa_id</c>,
/// convenção das <c>mobile_*</c>, #2180; <c>TenantId</c> e <c>LojaId</c>, #3108) sai das migrations
/// com RLS ligado e forçado e com a policy <c>tenant_isolation</c>. A <c>ocorrencias</c> nasceu sem o
/// bloco <c>DO $rls$</c> e ninguém viu; este teste pega a próxima tabela que esquecer.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class CoberturaRlsTests(PostgreSqlDatabaseFixture fixture)
{
    /// <summary>
    /// Isentas por desenho, com o motivo na ADR-0010 (Skip list). As duas primeiras vêm da migration
    /// AddRowLevelSecurity; as duas de log entraram no #3108, quando a guarda passou a enxergar <c>TenantId</c>.
    /// O prefixo <c>mobile_</c> saiu daqui no #2180 e <c>admin_impersonation_logs</c> no #3108 (já tinha RLS).
    /// </summary>
    private static readonly string[] IsentasPorDesenho =
        ["TenantFeatureFlags", "fatura_contador", "admin_audit_logs", "system_error_logs"];

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
             AND col.column_name IN ('EmpresaId', 'empresa_id', 'TenantId', 'LojaId')
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
            .ToList();
        faltando.Should().BeEmpty(
            $"tabela com EmpresaId sem RLS vaza entre empresas se o filtro do EF falhar; sem RLS: {string.Join(", ", faltando)}");

        // Lista de exceção que sobra é permissão sem dono: quem ganhou policy sai daqui.
        IsentasPorDesenho.Should().BeSubsetOf(semRls,
            "tabela que já tem RLS forçada e tenant_isolation não pode continuar listada como exceção");
    }

    /// <summary>
    /// Tabelas <c>mobile_*</c> sem coluna de empresa e sem policy (#2180). Só as <c>mobile_push_*</c>:
    /// avisos pré-pareamento por instalação (chave pública, token e recibo assinado), sem dado de loja.
    /// As filhas <c>mobile_order_items</c> e <c>mobile_batch_items</c> guardam dado de loja e recebem a
    /// policy pela tabela mãe. Tabela Mobile nova sem policy precisa entrar aqui de propósito.
    /// </summary>
    private static readonly string[] MobileGlobaisSemPolicy =
        ["mobile_push_installations", "mobile_push_messages", "mobile_push_provider_events", "mobile_push_receipts"];

    [SkippableFact]
    public async Task TabelaMobileSemPolicyEstaNaListaExplicita()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var semPolicy = await db.Database.SqlQueryRaw<string>("""
            SELECT c.relname AS "Value"
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema()
              AND c.relkind = 'r'
              AND c.relname LIKE 'mobile\_%'
              AND NOT (c.relrowsecurity AND c.relforcerowsecurity
                       AND EXISTS (SELECT 1 FROM pg_policies p
                                   WHERE p.schemaname = n.nspname AND p.tablename = c.relname
                                     AND p.policyname = 'tenant_isolation'))
            ORDER BY c.relname
            """).ToListAsync();

        semPolicy.Should().BeSubsetOf(MobileGlobaisSemPolicy,
            "tabela mobile_ sem RLS forçada e tenant_isolation precisa ser decisão explícita");
    }
}
