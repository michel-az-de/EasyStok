using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// RLS (ADR-0010 / camada 2) nas tabelas <c>mobile_*</c> com empresa (#2180). A
    /// <c>AddRowLevelSecurity</c> pulou o prefixo <c>mobile_</c> e o isolamento ficou no <c>Where</c>
    /// manual de cada controller (o #1509 vazou no pull do sync). Policy igual à do núcleo, com a
    /// coluna de cada tabela: <c>empresa_id</c> nas oito do SQL do Mobile e <c>"EmpresaId"</c> em
    /// <c>mobile_processed_mutations</c>. Idempotente (ENABLE/FORCE no-op se ligados, DROP IF EXISTS
    /// antes do CREATE) e fail-closed sem tenant: linha com empresa NULL só aparece com bypass.
    /// As filhas sem coluna de empresa (<c>mobile_order_items</c> e <c>mobile_batch_items</c>) guardam
    /// dado de loja e recebem a policy pela tabela mãe. Só as <c>mobile_push_*</c> ficam fora (avisos
    /// pré-pareamento, sem dado de loja); a lista explícita está em <c>CoberturaRlsTests</c>.
    /// </summary>
    public partial class AddRlsTabelasMobile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
BEGIN
    FOR rec IN
        SELECT c.table_schema, c.table_name, c.column_name
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema
         AND t.table_name   = c.table_name
        WHERE c.column_name IN ('empresa_id', 'EmpresaId')
          AND c.table_schema = current_schema()
          AND t.table_type   = 'BASE TABLE'
          AND c.table_name LIKE 'mobile\_%' ESCAPE '\'
        ORDER BY c.table_name
    LOOP
        EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', rec.table_schema, rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I FORCE ROW LEVEL SECURITY', rec.table_schema, rec.table_name);
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.%I', rec.table_schema, rec.table_name);
        EXECUTE format($pol$
            CREATE POLICY tenant_isolation ON %I.%I
                USING (
                    current_setting('app.bypass_rls', true) = 'true'
                    OR %I = NULLIF(current_setting('app.empresa_id', true), '')::uuid
                )
                WITH CHECK (
                    current_setting('app.bypass_rls', true) = 'true'
                    OR %I = NULLIF(current_setting('app.empresa_id', true), '')::uuid
                )
        $pol$, rec.table_schema, rec.table_name, rec.column_name, rec.column_name);
    END LOOP;

    -- Filhas sem coluna de empresa, com dado de loja: a linha só aparece se a mãe aparece. O EXISTS
    -- passa pela policy da mãe (tenant ou bypass), então não repete a regra aqui.
    FOR rec IN
        SELECT * FROM (VALUES ('mobile_order_items', 'order_id', 'mobile_orders'),
                              ('mobile_batch_items', 'batch_id', 'mobile_batches')) AS f(table_name, column_name, mae)
        WHERE to_regclass(format('%I.%I', current_schema(), f.table_name)) IS NOT NULL
    LOOP
        EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', current_schema(), rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I FORCE ROW LEVEL SECURITY', current_schema(), rec.table_name);
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.%I', current_schema(), rec.table_name);
        EXECUTE format($pol$
            CREATE POLICY tenant_isolation ON %1$I.%2$I
                USING (EXISTS (SELECT 1 FROM %1$I.%4$I m WHERE m."Id" = %3$I))
                WITH CHECK (EXISTS (SELECT 1 FROM %1$I.%4$I m WHERE m."Id" = %3$I))
        $pol$, current_schema(), rec.table_name, rec.column_name, rec.mae);
    END LOOP;
END
$rls$;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DO $rls_down$
DECLARE
    rec RECORD;
BEGIN
    FOR rec IN
        SELECT DISTINCT c.table_schema, c.table_name
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema
         AND t.table_name   = c.table_name
        WHERE c.column_name IN ('empresa_id', 'EmpresaId')
          AND c.table_schema = current_schema()
          AND t.table_type   = 'BASE TABLE'
          AND c.table_name LIKE 'mobile\_%' ESCAPE '\'
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.%I', rec.table_schema, rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I NO FORCE ROW LEVEL SECURITY', rec.table_schema, rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I DISABLE ROW LEVEL SECURITY', rec.table_schema, rec.table_name);
    END LOOP;

    FOR rec IN
        SELECT c.relname AS table_name FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = current_schema() AND c.relkind = 'r' AND c.relname IN ('mobile_order_items', 'mobile_batch_items')
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.%I', current_schema(), rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I NO FORCE ROW LEVEL SECURITY', current_schema(), rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I DISABLE ROW LEVEL SECURITY', current_schema(), rec.table_name);
    END LOOP;
END
$rls_down$;
""");
        }
    }
}
