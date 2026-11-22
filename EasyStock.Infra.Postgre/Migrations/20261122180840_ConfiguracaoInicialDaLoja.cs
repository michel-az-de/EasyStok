using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// Configuração inicial da loja (#3179, fatia 2 da #2070): três colunas anuláveis em <c>empresas</c> e a tabela
    /// <c>configuracao_inicial_passos</c>, com RLS por empresa. Só aditiva, sem backfill.
    /// </summary>
    public partial class ConfiguracaoInicialDaLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            // RLS (ADR-0010): a tabela nasce isolada por empresa, fail-closed sem tenant. O scaffold não gera policy.
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
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON configuracao_inicial_passos;");

            migrationBuilder.DropTable(
                name: "configuracao_inicial_passos");

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
