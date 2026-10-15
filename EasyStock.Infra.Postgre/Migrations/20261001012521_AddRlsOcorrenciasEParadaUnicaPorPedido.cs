using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddRlsOcorrenciasEParadaUnicaPorPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_viagem_paradas_pedido_ativo",
                table: "viagem_paradas",
                column: "PedidoId",
                unique: true,
                filter: "\"EntregueEm\" IS NULL");

            // RLS (ADR-0010 / camada 2) em ocorrencias, que nasceu sem o bloco (#1238; banco local media
            // relrowsecurity = f). Mesmo bloco de 20260922130316_AddAtendimentoConversaMensagem: idempotente
            // (ENABLE/FORCE no-op se ligados, DROP IF EXISTS antes do CREATE) e fail-closed sem tenant.
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'ocorrencias'
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
        EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', rec.table_schema, rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I FORCE ROW LEVEL SECURITY', rec.table_schema, rec.table_name);
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.%I', rec.table_schema, rec.table_name);
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
            migrationBuilder.Sql("""
DROP POLICY IF EXISTS tenant_isolation ON ocorrencias;
ALTER TABLE ocorrencias NO FORCE ROW LEVEL SECURITY;
ALTER TABLE ocorrencias DISABLE ROW LEVEL SECURITY;
""");

            migrationBuilder.DropIndex(
                name: "ux_viagem_paradas_pedido_ativo",
                table: "viagem_paradas");
        }
    }
}
