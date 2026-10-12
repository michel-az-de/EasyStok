using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddMensagemProgramada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Programada",
                table: "atendimento_mensagens",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "mensagens_programadas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Canal = table.Column<int>(type: "integer", nullable: false),
                    Finalidade = table.Column<int>(type: "integer", nullable: false),
                    Texto = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    ModeloNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ModeloIdioma = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ModeloParametrosJson = table.Column<string>(type: "jsonb", nullable: false),
                    AgendadaPara = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Situacao = table.Column<int>(type: "integer", nullable: false),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    IdExterno = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadaPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EnviadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AlteradaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mensagens_programadas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mensagens_programadas_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mensagens_programadas_ClienteId",
                table: "mensagens_programadas",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "ix_mensagens_programadas_empresa_cliente",
                table: "mensagens_programadas",
                columns: new[] { "EmpresaId", "ClienteId", "AgendadaPara" });

            migrationBuilder.CreateIndex(
                name: "ix_mensagens_programadas_situacao_agendada_para",
                table: "mensagens_programadas",
                columns: new[] { "Situacao", "AgendadaPara" });

            // RLS (ADR-0010, defesa em profundidade) — mesmo padrão de AddStorefront/
            // AddRowLevelSecurity para toda tabela nova com EmpresaId.
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'mensagens_programadas'
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
        EXECUTE format(
            'ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY',
            rec.table_schema, rec.table_name);

        EXECUTE format(
            'ALTER TABLE %I.%I FORCE ROW LEVEL SECURITY',
            rec.table_schema, rec.table_name);

        EXECUTE format(
            'DROP POLICY IF EXISTS tenant_isolation ON %I.%I',
            rec.table_schema, rec.table_name);

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
            migrationBuilder.DropTable(
                name: "mensagens_programadas");

            migrationBuilder.DropColumn(
                name: "Programada",
                table: "atendimento_mensagens");
        }
    }
}
