using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class PrimeiroAcessoDonaEHierarquia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EhDona",
                table: "usuarios_empresas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "EmpresaDaSessaoId",
                table: "refresh_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfiguracaoInicialAdiadaEm",
                table: "empresas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfiguracaoInicialConcluidaEm",
                table: "empresas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfiguracaoInicialConcluidaPorId",
                table: "empresas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "configuracao_inicial_passos",
                columns: table => new
                {
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Passo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PorUsuarioId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuracao_inicial_passos", x => new { x.EmpresaId, x.Passo });
                    table.CheckConstraint("ck_configuracao_inicial_passos_situacao", "\"Situacao\" IN ('conferido', 'depois')");
                    table.ForeignKey(
                        name: "FK_configuracao_inicial_passos_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // RLS (ADR-0010, defesa em profundidade): mesmo padrão de toda tabela nova com EmpresaId (#2070).
            // SQL manual: o scaffold do EF não gera este bloco; quem regenerar a migração porta à mão.
            migrationBuilder.Sql("""
ALTER TABLE configuracao_inicial_passos ENABLE ROW LEVEL SECURITY;
ALTER TABLE configuracao_inicial_passos FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON configuracao_inicial_passos;
CREATE POLICY tenant_isolation ON configuracao_inicial_passos
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
            // Inverso do bloco de RLS do Up, antes do DROP TABLE (que derrubaria a policy de qualquer forma).
            migrationBuilder.Sql("""
DROP POLICY IF EXISTS tenant_isolation ON configuracao_inicial_passos;
ALTER TABLE configuracao_inicial_passos NO FORCE ROW LEVEL SECURITY;
ALTER TABLE configuracao_inicial_passos DISABLE ROW LEVEL SECURITY;
""");

            migrationBuilder.DropTable(
                name: "configuracao_inicial_passos");

            migrationBuilder.DropColumn(
                name: "EhDona",
                table: "usuarios_empresas");

            migrationBuilder.DropColumn(
                name: "EmpresaDaSessaoId",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "ConfiguracaoInicialAdiadaEm",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "ConfiguracaoInicialConcluidaEm",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "ConfiguracaoInicialConcluidaPorId",
                table: "empresas");
        }
    }
}
