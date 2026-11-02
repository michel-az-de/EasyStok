using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class RlsSessoesNativas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE sessoes_nativas ENABLE ROW LEVEL SECURITY;
                ALTER TABLE sessoes_nativas FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON sessoes_nativas
                USING (current_setting('app.bypass_rls', true) = 'true'
                    OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid)
                WITH CHECK (current_setting('app.bypass_rls', true) = 'true'
                    OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS tenant_isolation ON sessoes_nativas;
                ALTER TABLE sessoes_nativas NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE sessoes_nativas DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
