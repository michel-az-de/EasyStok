using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class CompletarFaturaCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FinanceiroHabilitado",
                table: "empresas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<decimal>(
                name: "Valor",
                table: "CobrancasAssinatura",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddColumn<Guid>(
                name: "FaturaId",
                table: "CobrancasAssinatura",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FaturaId",
                table: "admin_tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fatura_contador",
                columns: table => new
                {
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ano = table.Column<int>(type: "integer", nullable: false),
                    UltimoNumero = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fatura_contador", x => new { x.EmpresaId, x.Ano });
                });

            migrationBuilder.CreateTable(
                name: "faturas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: true),
                    dados_faturado = table.Column<string>(type: "jsonb", nullable: false),
                    dados_emissor = table.Column<string>(type: "jsonb", nullable: false),
                    dados_fiscais = table.Column<string>(type: "jsonb", nullable: true),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrigemRefId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DataEmissao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataVencimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataPagamentoTotal = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubTotal = table.Column<decimal>(type: "numeric(14,2)", nullable: false, defaultValue: 0m),
                    Descontos = table.Column<decimal>(type: "numeric(14,2)", nullable: false, defaultValue: 0m),
                    Acrescimos = table.Column<decimal>(type: "numeric(14,2)", nullable: false, defaultValue: 0m),
                    Total = table.Column<decimal>(type: "numeric(14,2)", nullable: false, defaultValue: 0m),
                    Moeda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "BRL"),
                    Observacoes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    TicketRelacionadoId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    PdfStorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faturas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_faturas_admin_tickets_TicketRelacionadoId",
                        column: x => x.TicketRelacionadoId,
                        principalTable: "admin_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_faturas_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_faturas_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "webhook_recebidos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EventIdExterno = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RawBodyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecebidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Sucesso = table.Column<bool>(type: "boolean", nullable: false),
                    Erro = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_recebidos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fatura_eventos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FaturaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ValorAntes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ValorDepois = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MetadadosJson = table.Column<string>(type: "jsonb", nullable: true),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    UsuarioNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OcorridoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fatura_eventos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fatura_eventos_faturas_FaturaId",
                        column: x => x.FaturaId,
                        principalTable: "faturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fatura_itens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FaturaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Quantidade = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    PrecoUnitario = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fatura_itens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fatura_itens_faturas_FaturaId",
                        column: x => x.FaturaId,
                        principalTable: "faturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fatura_pagamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FaturaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Metodo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    GatewayProvedor = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    GatewayTransactionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DadosGatewayJson = table.Column<string>(type: "jsonb", nullable: true),
                    PagoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RegistradoPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegistradoPorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Observacao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fatura_pagamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fatura_pagamentos_faturas_FaturaId",
                        column: x => x.FaturaId,
                        principalTable: "faturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_assinatura_fatura_id",
                table: "CobrancasAssinatura",
                column: "FaturaId",
                filter: "\"FaturaId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_fatura_id",
                table: "admin_tickets",
                column: "FaturaId",
                filter: "\"FaturaId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fatura_eventos_fatura_id",
                table: "fatura_eventos",
                column: "FaturaId");

            migrationBuilder.CreateIndex(
                name: "ix_fatura_eventos_fatura_ocorrido",
                table: "fatura_eventos",
                columns: new[] { "FaturaId", "OcorridoEm" });

            migrationBuilder.CreateIndex(
                name: "ix_fatura_itens_fatura_id",
                table: "fatura_itens",
                column: "FaturaId");

            migrationBuilder.CreateIndex(
                name: "ix_fatura_itens_fatura_ordem",
                table: "fatura_itens",
                columns: new[] { "FaturaId", "Ordem" });

            migrationBuilder.CreateIndex(
                name: "ix_fatura_pagamentos_fatura_id",
                table: "fatura_pagamentos",
                column: "FaturaId");

            migrationBuilder.CreateIndex(
                name: "ix_fatura_pagamentos_fatura_status",
                table: "fatura_pagamentos",
                columns: new[] { "FaturaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_fatura_pagamentos_gateway_tx",
                table: "fatura_pagamentos",
                columns: new[] { "GatewayProvedor", "GatewayTransactionId" },
                filter: "\"GatewayTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_faturas_ClienteId",
                table: "faturas",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "ix_faturas_empresa_status",
                table: "faturas",
                columns: new[] { "EmpresaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_faturas_empresa_vencimento",
                table: "faturas",
                columns: new[] { "EmpresaId", "DataVencimento" });

            migrationBuilder.CreateIndex(
                name: "ix_faturas_origem_ref",
                table: "faturas",
                columns: new[] { "Origem", "OrigemRefId" });

            migrationBuilder.CreateIndex(
                name: "ix_faturas_ticket_relacionado",
                table: "faturas",
                column: "TicketRelacionadoId",
                filter: "\"TicketRelacionadoId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_faturas_empresa_numero",
                table: "faturas",
                columns: new[] { "EmpresaId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_recebidos_recebido_em",
                table: "webhook_recebidos",
                column: "RecebidoEm");

            migrationBuilder.CreateIndex(
                name: "ux_webhook_recebidos_provedor_eventid",
                table: "webhook_recebidos",
                columns: new[] { "Provedor", "EventIdExterno" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_admin_tickets_faturas_FaturaId",
                table: "admin_tickets",
                column: "FaturaId",
                principalTable: "faturas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CobrancasAssinatura_faturas_FaturaId",
                table: "CobrancasAssinatura",
                column: "FaturaId",
                principalTable: "faturas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_admin_tickets_faturas_FaturaId",
                table: "admin_tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_CobrancasAssinatura_faturas_FaturaId",
                table: "CobrancasAssinatura");

            migrationBuilder.DropTable(
                name: "fatura_contador");

            migrationBuilder.DropTable(
                name: "fatura_eventos");

            migrationBuilder.DropTable(
                name: "fatura_itens");

            migrationBuilder.DropTable(
                name: "fatura_pagamentos");

            migrationBuilder.DropTable(
                name: "webhook_recebidos");

            migrationBuilder.DropTable(
                name: "faturas");

            migrationBuilder.DropIndex(
                name: "ix_cobrancas_assinatura_fatura_id",
                table: "CobrancasAssinatura");

            migrationBuilder.DropIndex(
                name: "ix_admin_tickets_fatura_id",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "FinanceiroHabilitado",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "FaturaId",
                table: "CobrancasAssinatura");

            migrationBuilder.DropColumn(
                name: "FaturaId",
                table: "admin_tickets");

            migrationBuilder.AlterColumn<decimal>(
                name: "Valor",
                table: "CobrancasAssinatura",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,2)",
                oldPrecision: 14,
                oldScale: 2);
        }
    }
}
