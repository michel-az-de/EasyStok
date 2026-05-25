using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// Backfill de Row-Level Security para 3 tabelas com <c>EmpresaId</c> criadas
    /// em migrations posteriores à <c>20260511120000_AddRowLevelSecurity</c>:
    /// <list type="bullet">
    ///   <item><c>notif_web_push_subscriptions</c> (de <c>20260516014449_AddWebPushSubscription</c>)</item>
    ///   <item><c>produtos_composicao</c> (de <c>20260516014645_AddProdutoComposicaoEUnidadeMedida</c>)</item>
    ///   <item><c>produtos_composicao_alteracao</c> (idem)</item>
    /// </list>
    ///
    /// <para>
    /// Por que existe: a <c>AddRowLevelSecurity</c> faz um snapshot do
    /// <c>information_schema</c> no momento em que roda — ela só protege tabelas
    /// existentes naquele instante. Tabelas adicionadas depois ficam sem policy,
    /// dependendo apenas do Global Query Filter do EF (camada única). O
    /// diagnóstico em <c>docs/dev/incidentes/2026-05-22-rls-prod-role-status.md</c>
    /// confirmou que em prod (PR #200) essas 3 tabelas ficaram fora.
    /// </para>
    ///
    /// <para>
    /// O SQL aqui reproduz o mesmo padrão da migration original (ENABLE +
    /// FORCE + policy <c>tenant_isolation</c> com <c>NULLIF(...)::uuid</c>
    /// para fail-closed quando o contexto de tenant não foi setado), apenas
    /// restrito à lista explícita dessas 3 tabelas. Idempotente: pode rodar
    /// múltiplas vezes sem efeito colateral — <c>ALTER TABLE ENABLE/FORCE</c> é
    /// no-op se já habilitado, e a policy usa <c>DROP POLICY IF EXISTS</c>
    /// antes do <c>CREATE POLICY</c>.
    /// </para>
    ///
    /// <para>
    /// Observação sobre <c>notif_web_push_subscriptions</c>: a coluna
    /// <c>EmpresaId</c> dessa tabela é <c>NULLABLE</c> (subscription anônima
    /// quando ambos EmpresaId/UsuarioId são null). A policy filtra rows com
    /// <c>EmpresaId IS NULL</c> (comparação retorna UNKNOWN → fail-closed),
    /// que é o comportamento desejado: anônimo não pertence a tenant.
    /// </para>
    /// </summary>
    public partial class AddRlsBackfillProdutosComposicaoNotifPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Raw string literal (""""..."""") — o SQL contém $rls$ e $pol$ como
            // dollar-quoted strings do Postgres, que conflitam com $"..." em C#.
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'notif_web_push_subscriptions',
        'produtos_composicao',
        'produtos_composicao_alteracao'
    ];
BEGIN
    FOR rec IN
        SELECT c.table_schema, c.table_name
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema
         AND t.table_name   = c.table_name
        WHERE c.column_name = 'EmpresaId'
          AND c.table_schema = current_schema()
          AND t.table_type   = 'BASE TABLE'
          AND c.table_name = ANY(target_tables)
        ORDER BY c.table_name
    LOOP
        EXECUTE format(
            'ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY',
            rec.table_schema, rec.table_name);

        -- FORCE garante que mesmo o owner da tabela respeita a policy.
        -- Sem isso, o usuário que criou a tabela (geralmente o app) bypassa RLS
        -- por default — exatamente o que queremos evitar.
        EXECUTE format(
            'ALTER TABLE %I.%I FORCE ROW LEVEL SECURITY',
            rec.table_schema, rec.table_name);

        EXECUTE format(
            'DROP POLICY IF EXISTS tenant_isolation ON %I.%I',
            rec.table_schema, rec.table_name);

        -- USING (SELECT/UPDATE/DELETE visibility): bypass OU tenant casa.
        -- WITH CHECK (INSERT/UPDATE writability): mesma regra — evita escrita
        -- cross-tenant mesmo via UPDATE que mudaria EmpresaId.
        -- NULLIF('','')::uuid trata o caso de connection nova de pool sem tenant
        -- setado: current_setting retorna '' (missing_ok=true), NULLIF vira NULL,
        -- comparação com NULL é UNKNOWN/false → 0 linhas (fail-closed).
        EXECUTE format($pol$
            CREATE POLICY tenant_isolation ON %I.%I
                USING (
                    current_setting('app.bypass_rls', true) = 'true'
                    OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid
                )
                WITH CHECK (
                    current_setting('app.bypass_rls', true) = 'true'
                    OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid
                )
        $pol$, rec.table_schema, rec.table_name);
    END LOOP;
END
$rls$;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverte apenas as 3 tabelas alvo. Idempotente: DROP POLICY IF EXISTS
            // e NO FORCE / DISABLE são no-op se a tabela não está protegida.
            migrationBuilder.Sql("""
DO $rls_down$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'notif_web_push_subscriptions',
        'produtos_composicao',
        'produtos_composicao_alteracao'
    ];
BEGIN
    FOR rec IN
        SELECT c.table_schema, c.table_name
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema
         AND t.table_name   = c.table_name
        WHERE c.column_name = 'EmpresaId'
          AND c.table_schema = current_schema()
          AND t.table_type   = 'BASE TABLE'
          AND c.table_name = ANY(target_tables)
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.%I',
            rec.table_schema, rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I NO FORCE ROW LEVEL SECURITY',
            rec.table_schema, rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I DISABLE ROW LEVEL SECURITY',
            rec.table_schema, rec.table_name);
    END LOOP;
END
$rls_down$;
""");
        }
    }
}
