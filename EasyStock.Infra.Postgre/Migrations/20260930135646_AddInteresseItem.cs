using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddInteresseItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "interesses_item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardapioItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RegistradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtendidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interesses_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_interesses_item_cardapio_item_CardapioItemId",
                        column: x => x.CardapioItemId,
                        principalTable: "cardapio_item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_interesses_item_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_interesses_item_abertos_por_item",
                table: "interesses_item",
                columns: new[] { "EmpresaId", "CardapioItemId" },
                filter: "\"AtendidoEm\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_interesses_item_CardapioItemId",
                table: "interesses_item",
                column: "CardapioItemId");

            migrationBuilder.CreateIndex(
                name: "IX_interesses_item_ClienteId",
                table: "interesses_item",
                column: "ClienteId");

            // RLS (ADR-0010, defesa em profundidade) — mesmo padrão de AddConsentimentoContato
            // para toda tabela nova com EmpresaId.
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'interesses_item'
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
                name: "interesses_item");
        }
    }
}
