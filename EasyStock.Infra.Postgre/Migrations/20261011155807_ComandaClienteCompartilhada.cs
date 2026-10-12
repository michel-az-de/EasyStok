using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class ComandaClienteCompartilhada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PedidoRegistradoId",
                table: "links_cardapio_conversa",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RascunhoJson",
                table: "links_cardapio_conversa",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "ResultadoJson",
                table: "links_cardapio_conversa",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revisao",
                table: "links_cardapio_conversa",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Complementar",
                table: "cobrancas_pedido",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "creditos_cliente_comanda",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoOrigemId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperacaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Saldo = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_creditos_cliente_comanda", x => x.Id);
                    table.ForeignKey(
                        name: "FK_creditos_cliente_comanda_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_creditos_cliente_comanda_pedidos_PedidoOrigemId",
                        column: x => x.PedidoOrigemId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "usos_credito_cliente_comanda",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditoId = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usos_credito_cliente_comanda", x => x.Id);
                    table.ForeignKey(
                        name: "FK_usos_credito_cliente_comanda_creditos_cliente_comanda_Credi~",
                        column: x => x.CreditoId,
                        principalTable: "creditos_cliente_comanda",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_usos_credito_cliente_comanda_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_creditos_cliente_comanda_ClienteId",
                table: "creditos_cliente_comanda",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_creditos_cliente_comanda_EmpresaId_Codigo",
                table: "creditos_cliente_comanda",
                columns: new[] { "EmpresaId", "Codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_creditos_cliente_comanda_EmpresaId_OperacaoId",
                table: "creditos_cliente_comanda",
                columns: new[] { "EmpresaId", "OperacaoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_creditos_cliente_comanda_PedidoOrigemId",
                table: "creditos_cliente_comanda",
                column: "PedidoOrigemId");

            migrationBuilder.CreateIndex(
                name: "IX_usos_credito_cliente_comanda_CreditoId",
                table: "usos_credito_cliente_comanda",
                column: "CreditoId");

            migrationBuilder.CreateIndex(
                name: "IX_usos_credito_cliente_comanda_EmpresaId_PedidoId",
                table: "usos_credito_cliente_comanda",
                columns: new[] { "EmpresaId", "PedidoId" });

            migrationBuilder.CreateIndex(
                name: "IX_usos_credito_cliente_comanda_PedidoId",
                table: "usos_credito_cliente_comanda",
                column: "PedidoId");
            foreach (var table in new[] { "creditos_cliente_comanda", "usos_credito_cliente_comanda" })
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON {table}
                    USING (current_setting('app.bypass_rls', true) = 'true'
                      OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid)
                    WITH CHECK (current_setting('app.bypass_rls', true) = 'true'
                      OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid);
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usos_credito_cliente_comanda");

            migrationBuilder.DropTable(
                name: "creditos_cliente_comanda");

            migrationBuilder.DropColumn(
                name: "PedidoRegistradoId",
                table: "links_cardapio_conversa");

            migrationBuilder.DropColumn(
                name: "RascunhoJson",
                table: "links_cardapio_conversa");

            migrationBuilder.DropColumn(
                name: "ResultadoJson",
                table: "links_cardapio_conversa");

            migrationBuilder.DropColumn(
                name: "Revisao",
                table: "links_cardapio_conversa");

            migrationBuilder.DropColumn(
                name: "Complementar",
                table: "cobrancas_pedido");
        }
    }
}
