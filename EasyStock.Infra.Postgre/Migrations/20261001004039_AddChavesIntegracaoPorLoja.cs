using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddChavesIntegracaoPorLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "mascara",
                table: "credencial_integracao",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ultimo_teste_em",
                table: "credencial_integracao",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ultimo_teste_mensagem",
                table: "credencial_integracao",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ultimo_teste_ok",
                table: "credencial_integracao",
                type: "boolean",
                nullable: true);

            // RLS (ADR-0010, F16 #1246): a tabela nasceu sem policy e a migration geral de RLS só
            // casa a coluna "EmpresaId"; aqui a coluna é empresa_id (snake_case). Mesmo predicado
            // das demais: bypass explícito do host ou o tenant da sessão (app.empresa_id).
            migrationBuilder.Sql("""
ALTER TABLE credencial_integracao ENABLE ROW LEVEL SECURITY;
ALTER TABLE credencial_integracao FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON credencial_integracao;
CREATE POLICY tenant_isolation ON credencial_integracao
    USING (
        current_setting('app.bypass_rls', true) = 'true'
        OR empresa_id = NULLIF(current_setting('app.empresa_id', true), '')::uuid
    )
    WITH CHECK (
        current_setting('app.bypass_rls', true) = 'true'
        OR empresa_id = NULLIF(current_setting('app.empresa_id', true), '')::uuid
    );
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DROP POLICY IF EXISTS tenant_isolation ON credencial_integracao;
ALTER TABLE credencial_integracao NO FORCE ROW LEVEL SECURITY;
ALTER TABLE credencial_integracao DISABLE ROW LEVEL SECURITY;
""");

            migrationBuilder.DropColumn(
                name: "mascara",
                table: "credencial_integracao");

            migrationBuilder.DropColumn(
                name: "ultimo_teste_em",
                table: "credencial_integracao");

            migrationBuilder.DropColumn(
                name: "ultimo_teste_mensagem",
                table: "credencial_integracao");

            migrationBuilder.DropColumn(
                name: "ultimo_teste_ok",
                table: "credencial_integracao");
        }
    }
}
