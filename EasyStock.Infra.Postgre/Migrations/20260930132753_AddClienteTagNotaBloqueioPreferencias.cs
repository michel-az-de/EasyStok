using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddClienteTagNotaBloqueioPreferencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AvisosStatusAtivos",
                table: "clientes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Bloqueado",
                table: "clientes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "BloqueadoEm",
                table: "clientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoBloqueio",
                table: "clientes",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cliente_notas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Texto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: true),
                    MensagemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Autor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cliente_notas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cliente_notas_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cliente_tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cliente_tags", x => x.Id);
                    table.CheckConstraint("ck_cliente_tags_origem", "\"Origem\" IN ('dona','agente','sistema')");
                    table.ForeignKey(
                        name: "FK_cliente_tags_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cliente_notas_ClienteId",
                table: "cliente_notas",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_cliente_notas_EmpresaId_ClienteId_CriadoEm",
                table: "cliente_notas",
                columns: new[] { "EmpresaId", "ClienteId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_cliente_tags_EmpresaId_Tag",
                table: "cliente_tags",
                columns: new[] { "EmpresaId", "Tag" });

            migrationBuilder.CreateIndex(
                name: "uq_cliente_tags_cliente_tag",
                table: "cliente_tags",
                columns: new[] { "ClienteId", "Tag" },
                unique: true);

            // RLS (ADR-0010, defesa em profundidade) — mesmo padrão de AddConsentimentoContato para
            // toda tabela nova com EmpresaId.
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'cliente_tags',
        'cliente_notas'
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
                name: "cliente_notas");

            migrationBuilder.DropTable(
                name: "cliente_tags");

            migrationBuilder.DropColumn(
                name: "AvisosStatusAtivos",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "Bloqueado",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "BloqueadoEm",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "MotivoBloqueio",
                table: "clientes");
        }
    }
}
