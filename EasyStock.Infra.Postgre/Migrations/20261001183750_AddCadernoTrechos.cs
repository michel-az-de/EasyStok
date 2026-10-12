using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddCadernoTrechos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "caderno_trechos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Texto = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    PalavrasChave = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Nucleo = table.Column<bool>(type: "boolean", nullable: false),
                    Arquivado = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caderno_trechos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_caderno_trechos_empresa_arquivado",
                table: "caderno_trechos",
                columns: new[] { "EmpresaId", "Arquivado" });

            // RLS (ADR-0010, defesa em profundidade) — mesmo padrão de AddRespostasProntasEAutomacoes.
            migrationBuilder.Sql("""
ALTER TABLE caderno_trechos ENABLE ROW LEVEL SECURITY;
ALTER TABLE caderno_trechos FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON caderno_trechos;
CREATE POLICY tenant_isolation ON caderno_trechos
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
            migrationBuilder.DropTable(
                name: "caderno_trechos");
        }
    }
}
