using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class VinculeAuditoriaAEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmpresaId",
                table: "audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_EmpresaId_DataHora",
                table: "audit_logs",
                columns: new[] { "EmpresaId", "DataHora" });

            migrationBuilder.Sql("""
ALTER TABLE audit_logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_logs FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON audit_logs
    USING (current_setting('app.bypass_rls', true) = 'true'
        OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid)
    WITH CHECK (current_setting('app.bypass_rls', true) = 'true'
        OR "EmpresaId" IS NULL
        OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid);
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON audit_logs; ALTER TABLE audit_logs DISABLE ROW LEVEL SECURITY; ALTER TABLE audit_logs NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.DropIndex(
                name: "IX_audit_logs_EmpresaId_DataHora",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "audit_logs");
        }
    }
}
