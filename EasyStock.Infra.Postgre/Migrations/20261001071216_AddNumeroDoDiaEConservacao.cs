using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddNumeroDoDiaEConservacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "data_numero",
                table: "pedidos",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "numero_do_dia",
                table: "pedidos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConservacaoSnapshot",
                table: "pedido_itens",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Conservacao",
                table: "cardapio_item",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "sequencias_pedido_dia",
                columns: table => new
                {
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Ultimo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequencias_pedido_dia", x => new { x.EmpresaId, x.Data });
                    table.ForeignKey(
                        name: "FK_sequencias_pedido_dia_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_pedidos_empresa_numero_do_dia",
                table: "pedidos",
                columns: new[] { "EmpresaId", "data_numero", "numero_do_dia" },
                unique: true,
                filter: "numero_do_dia IS NOT NULL");

            // RLS (ADR-0010, defesa em profundidade): mesmo padrão de toda tabela nova com EmpresaId.
            migrationBuilder.Sql("""
ALTER TABLE sequencias_pedido_dia ENABLE ROW LEVEL SECURITY;
ALTER TABLE sequencias_pedido_dia FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON sequencias_pedido_dia;
CREATE POLICY tenant_isolation ON sequencias_pedido_dia
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
                name: "sequencias_pedido_dia");

            migrationBuilder.DropIndex(
                name: "ux_pedidos_empresa_numero_do_dia",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "data_numero",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "numero_do_dia",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "ConservacaoSnapshot",
                table: "pedido_itens");

            migrationBuilder.DropColumn(
                name: "Conservacao",
                table: "cardapio_item");
        }
    }
}
