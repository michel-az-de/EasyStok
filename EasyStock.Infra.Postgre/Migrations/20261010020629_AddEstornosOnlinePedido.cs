using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddEstornosOnlinePedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pedido_estornos_online",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    PagamentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    PagamentoExternoId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    UsuarioNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EstornoExternoId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Detalhe = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EnviadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MovimentoCaixaId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedido_estornos_online", x => x.Id);
                    table.CheckConstraint("ck_estorno_online_confirmacao", "(\"Situacao\" = 'confirmado') = (\"MovimentoCaixaId\" IS NOT NULL AND \"ConfirmadoEm\" IS NOT NULL)");
                    table.CheckConstraint("ck_estorno_online_valor", "\"Valor\" > 0");
                    table.ForeignKey(
                        name: "FK_pedido_estornos_online_movimentos_caixa_MovimentoCaixaId",
                        column: x => x.MovimentoCaixaId,
                        principalTable: "movimentos_caixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedido_estornos_online_pedido_pagamentos_PagamentoId",
                        column: x => x.PagamentoId,
                        principalTable: "pedido_pagamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedido_estornos_online_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pedido_estornos_online_EmpresaId_PagamentoExternoId_Estorno~",
                table: "pedido_estornos_online",
                columns: new[] { "EmpresaId", "PagamentoExternoId", "EstornoExternoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pedido_estornos_online_EmpresaId_PedidoId",
                table: "pedido_estornos_online",
                columns: new[] { "EmpresaId", "PedidoId" });

            migrationBuilder.CreateIndex(
                name: "IX_pedido_estornos_online_MovimentoCaixaId",
                table: "pedido_estornos_online",
                column: "MovimentoCaixaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pedido_estornos_online_PagamentoId",
                table: "pedido_estornos_online",
                column: "PagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_pedido_estornos_online_PedidoId",
                table: "pedido_estornos_online",
                column: "PedidoId");
            migrationBuilder.Sql("""
                ALTER TABLE pedido_estornos_online ENABLE ROW LEVEL SECURITY;
                ALTER TABLE pedido_estornos_online FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON pedido_estornos_online
                    USING (current_setting('app.bypass_rls', true) = 'true'
                        OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid)
                    WITH CHECK (current_setting('app.bypass_rls', true) = 'true'
                        OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Até uma intenção pendente pode ter sido aceita pelo provedor após perdermos a resposta.
            migrationBuilder.Sql("""
                SET LOCAL row_security = off;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM pedido_estornos_online) THEN
                        RAISE EXCEPTION 'Existem solicitações de estorno. Preserve a conciliação antes de reverter esta migration.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "pedido_estornos_online");
        }
    }
}
