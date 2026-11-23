using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddModelosMensagem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "modelos_mensagem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Idioma = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Cabecalho = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Corpo = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Rodape = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Arquivado = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modelos_mensagem", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_modelos_mensagem_empresa_nome_idioma",
                table: "modelos_mensagem",
                columns: new[] { "EmpresaId", "Nome", "Idioma" },
                unique: true);

            // RLS (ADR-0010): a tabela nasce isolada por empresa, fail-closed sem tenant. O scaffold não gera policy.
            migrationBuilder.Sql("""
ALTER TABLE modelos_mensagem ENABLE ROW LEVEL SECURITY;
ALTER TABLE modelos_mensagem FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON modelos_mensagem;
CREATE POLICY tenant_isolation ON modelos_mensagem
    USING (
        current_setting('app.bypass_rls', true) = 'true'
        OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid
    )
    WITH CHECK (
        current_setting('app.bypass_rls', true) = 'true'
        OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid
    );
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON modelos_mensagem;");

            migrationBuilder.DropTable(
                name: "modelos_mensagem");
        }
    }
}
