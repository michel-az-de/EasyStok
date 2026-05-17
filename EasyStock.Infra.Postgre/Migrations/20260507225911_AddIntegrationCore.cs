using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credencial_integracao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    categoria = table.Column<int>(type: "integer", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ambiente = table.Column<int>(type: "integer", nullable: false),
                    payload_cifrado = table.Column<byte[]>(type: "bytea", nullable: false),
                    kek_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    iv = table.Column<byte[]>(type: "bytea", nullable: false),
                    tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    valido_de = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valido_ate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    ultimo_uso_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    alterado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credencial_integracao", x => x.id);
                    table.ForeignKey(
                        name: "FK_credencial_integracao_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_evento_integracao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_evento = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    aggregate_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    payload_schema_version = table.Column<int>(type: "integer", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    causation_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    max_tentativas = table.Column<int>(type: "integer", nullable: false),
                    proxima_tentativa_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    erro_ultima_tentativa = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    shard_key = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_evento_integracao", x => x.id);
                    table.ForeignKey(
                        name: "FK_outbox_evento_integracao_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fatura_pagamentos_RegistradoPorUserId",
                table: "fatura_pagamentos",
                column: "RegistradoPorUserId");

            migrationBuilder.CreateIndex(
                name: "ix_credencial_integracao_empresa_provider_ambiente_ativo",
                table: "credencial_integracao",
                columns: new[] { "empresa_id", "provider_key", "ambiente" },
                unique: true,
                filter: "ativo = true");

            migrationBuilder.CreateIndex(
                name: "ix_credencial_integracao_kek_id",
                table: "credencial_integracao",
                column: "kek_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_evento_integracao_empresa_status",
                table: "outbox_evento_integracao",
                columns: new[] { "empresa_id", "status", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_evento_integracao_idempotency_key",
                table: "outbox_evento_integracao",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_evento_integracao_pendentes",
                table: "outbox_evento_integracao",
                columns: new[] { "shard_key", "proxima_tentativa_em" },
                filter: "status = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_fatura_pagamentos_usuarios_RegistradoPorUserId",
                table: "fatura_pagamentos",
                column: "RegistradoPorUserId",
                principalTable: "usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fatura_pagamentos_usuarios_RegistradoPorUserId",
                table: "fatura_pagamentos");

            migrationBuilder.DropTable(
                name: "credencial_integracao");

            migrationBuilder.DropTable(
                name: "outbox_evento_integracao");

            migrationBuilder.DropIndex(
                name: "IX_fatura_pagamentos_RegistradoPorUserId",
                table: "fatura_pagamentos");
        }
    }
}
