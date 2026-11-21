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
    /// Tabelas <c>mobile_*</c> sem empresa (itens e push) ficam fora; a lista explícita está em
    /// <c>CoberturaRlsTests</c>.
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
END
$rls_down$;
""");
        }
    }
}
