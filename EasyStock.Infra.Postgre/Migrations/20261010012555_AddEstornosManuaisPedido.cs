using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddEstornosManuaisPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pedido_estornos_manuais",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    PagamentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Metodo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Referencia = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RegistradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedido_estornos_manuais", x => x.Id);
                    table.CheckConstraint("ck_estorno_manual_valor", "\"Valor\" > 0");
                    table.ForeignKey(
                        name: "FK_pedido_estornos_manuais_movimentos_caixa_Id",
                        column: x => x.Id,
                        principalTable: "movimentos_caixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedido_estornos_manuais_pedido_pagamentos_PagamentoId",
                        column: x => x.PagamentoId,
                        principalTable: "pedido_pagamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedido_estornos_manuais_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pedido_estornos_manuais_EmpresaId_PedidoId",
                table: "pedido_estornos_manuais",
                columns: new[] { "EmpresaId", "PedidoId" });

            migrationBuilder.CreateIndex(
                name: "IX_pedido_estornos_manuais_PagamentoId",
                table: "pedido_estornos_manuais",
                column: "PagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_pedido_estornos_manuais_PedidoId",
                table: "pedido_estornos_manuais",
                column: "PedidoId");

            migrationBuilder.Sql("""
                ALTER TABLE pedido_estornos_manuais ENABLE ROW LEVEL SECURITY;
                ALTER TABLE pedido_estornos_manuais FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON pedido_estornos_manuais
                    USING (current_setting('app.bypass_rls', true) = 'true'
                        OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid)
                    WITH CHECK (current_setting('app.bypass_rls', true) = 'true'
                        OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Não perder o vínculo auditável com saídas do Caixa já confirmadas.
            // row_security=off falha em vez de esconder registros do guard.
            migrationBuilder.Sql("""
                SET LOCAL row_security = off;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM pedido_estornos_manuais) THEN
                        RAISE EXCEPTION 'Existem devoluções confirmadas. Preserve os registros antes de reverter esta migration.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "pedido_estornos_manuais");
        }
    }
}
