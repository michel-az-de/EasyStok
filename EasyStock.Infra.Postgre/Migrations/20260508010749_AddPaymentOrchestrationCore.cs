using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentOrchestrationCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─── Onda P0 Payment Orchestration ─────────────────────────────────
            // Colunas em fatura_pagamentos. EmpresaId precisa de backfill antes de
            // virar NOT NULL — caso contrario rows existentes ficam Guid.Empty e
            // o Global Query Filter elimina elas para todos os tenants.

            migrationBuilder.AddColumn<string>(
                name: "ClientIdempotencyKey",
                table: "fatura_pagamentos",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EmpresaId",
                table: "fatura_pagamentos",
                type: "uuid",
                nullable: true);

            // Backfill: copia EmpresaId da Fatura associada para cada pagamento
            // existente. Pagamentos legados sao mantidos com seu tenant correto.
            migrationBuilder.Sql(@"
                UPDATE fatura_pagamentos fp
                   SET ""EmpresaId"" = f.""EmpresaId""
                  FROM faturas f
                 WHERE fp.""FaturaId"" = f.""Id""
                   AND fp.""EmpresaId"" IS NULL;
            ");

            // Apos backfill, torna NOT NULL.
            migrationBuilder.AlterColumn<Guid>(
                name: "EmpresaId",
                table: "fatura_pagamentos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TentativaAtualId",
                table: "fatura_pagamentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalTentativas",
                table: "fatura_pagamentos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte>(
                name: "UltimaErrorCategory",
                table: "fatura_pagamentos",
                type: "smallint",
                nullable: true);

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
                name: "gateway_health_snapshots",
                columns: table => new
                {
                    Provedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Estado = table.Column<byte>(type: "smallint", nullable: false),
                    JanelaInicioEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    JanelaFimEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TotalAttempts = table.Column<int>(type: "integer", nullable: false),
                    SuccessCount = table.Column<int>(type: "integer", nullable: false),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    TimeoutCount = table.Column<int>(type: "integer", nullable: false),
                    RateLimitCount = table.Column<int>(type: "integer", nullable: false),
                    LatenciaP50Ms = table.Column<int>(type: "integer", nullable: false),
                    LatenciaP95Ms = table.Column<int>(type: "integer", nullable: false),
                    SuspensoAte = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimoCanaryEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimoErro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UltimoErroEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimoSucessoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gateway_health_snapshots", x => x.Provedor);
                });

            migrationBuilder.CreateTable(
                name: "gateway_routing_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Metodo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Provedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Prioridade = table.Column<int>(type: "integer", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Moeda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "BRL"),
                    Pais = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false, defaultValue: "BR"),
                    ValorMinimoCentavos = table.Column<long>(type: "bigint", nullable: true),
                    ValorMaximoCentavos = table.Column<long>(type: "bigint", nullable: true),
                    RegrasJson = table.Column<string>(type: "jsonb", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gateway_routing_rules", x => x.Id);
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

            migrationBuilder.CreateTable(
                name: "pagamento_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FaturaPagamentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    FaturaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CobrancaAssinaturaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Metodo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Tentativa = table.Column<int>(type: "integer", nullable: false),
                    IniciadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinalizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LatenciaMs = table.Column<int>(type: "integer", nullable: true),
                    GatewayTransactionId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ErrorCategory = table.Column<byte>(type: "smallint", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProximaConsultaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClientIdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RoutingMotivo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pagamento_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pagamento_attempts_fatura_pagamentos_FaturaPagamentoId",
                        column: x => x.FaturaPagamentoId,
                        principalTable: "fatura_pagamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pagamento_attempt_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentAttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Motivo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    GatewayResponseJson = table.Column<string>(type: "jsonb", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OcorridoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pagamento_attempt_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pagamento_attempt_events_pagamento_attempts_PaymentAttemptId",
                        column: x => x.PaymentAttemptId,
                        principalTable: "pagamento_attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fatura_pagamentos_empresa_status",
                table: "fatura_pagamentos",
                columns: new[] { "EmpresaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_fatura_pagamentos_RegistradoPorUserId",
                table: "fatura_pagamentos",
                column: "RegistradoPorUserId");

            migrationBuilder.CreateIndex(
                name: "ux_fatura_pagamentos_empresa_client_idempotency",
                table: "fatura_pagamentos",
                columns: new[] { "EmpresaId", "ClientIdempotencyKey" },
                unique: true,
                filter: "\"ClientIdempotencyKey\" IS NOT NULL");

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
                name: "ix_gateway_routing_rules_empresa_metodo_ativo_prioridade",
                table: "gateway_routing_rules",
                columns: new[] { "EmpresaId", "Metodo", "Ativo", "Prioridade" });

            migrationBuilder.CreateIndex(
                name: "ux_gateway_routing_rules_empresa_metodo_provedor_moeda_pais",
                table: "gateway_routing_rules",
                columns: new[] { "EmpresaId", "Metodo", "Provedor", "Moeda", "Pais" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "ix_pagamento_attempt_events_attempt_ocorrido",
                table: "pagamento_attempt_events",
                columns: new[] { "PaymentAttemptId", "OcorridoEm" });

            migrationBuilder.CreateIndex(
                name: "ix_pagamento_attempts_empresa_pagamento_tentativa",
                table: "pagamento_attempts",
                columns: new[] { "EmpresaId", "FaturaPagamentoId", "Tentativa" });

            migrationBuilder.CreateIndex(
                name: "ix_pagamento_attempts_empresa_provedor_inicio",
                table: "pagamento_attempts",
                columns: new[] { "EmpresaId", "Provedor", "IniciadoEm" },
                filter: "\"Status\" <> 'Sucesso'");

            migrationBuilder.CreateIndex(
                name: "ix_pagamento_attempts_status_proxima_consulta",
                table: "pagamento_attempts",
                columns: new[] { "Status", "ProximaConsultaEm" });

            migrationBuilder.CreateIndex(
                name: "ux_pagamento_attempts_empresa_idempotency",
                table: "pagamento_attempts",
                columns: new[] { "EmpresaId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_pagamento_attempts_gateway_tx",
                table: "pagamento_attempts",
                columns: new[] { "Provedor", "GatewayTransactionId" },
                unique: true,
                filter: "\"GatewayTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_pagamento_attempts_pagamento_sucesso",
                table: "pagamento_attempts",
                column: "FaturaPagamentoId",
                unique: true,
                filter: "\"Status\" = 'Sucesso'");

            migrationBuilder.AddForeignKey(
                name: "FK_fatura_pagamentos_usuarios_RegistradoPorUserId",
                table: "fatura_pagamentos",
                column: "RegistradoPorUserId",
                principalTable: "usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ─── Seed regras globais Onda P0 ────────────────────────────────────
            // Preserva comportamento atual: EfiPix prio=1 pra "pix"; Manual
            // prio=99 pra "manual"/"dinheiro"/"transferencia"/"cheque"/"outro"
            // (cobre todos os metodos que ManualGatewayAdapter.SuportaMetodo
            // aceita). EfiBoleto, Stripe e MercadoPago entram em P2 quando
            // os adapters reais (com Stripe.net / SDK MP) forem implementados.
            // Guids fixos pra idempotencia da migration — re-runs viram no-op.
            migrationBuilder.Sql(@"
                INSERT INTO gateway_routing_rules
                    (""Id"", ""EmpresaId"", ""Metodo"", ""Provedor"", ""Prioridade"",
                     ""Ativo"", ""Moeda"", ""Pais"", ""ValorMinimoCentavos"",
                     ""ValorMaximoCentavos"", ""RegrasJson"", ""CriadoEm"", ""AtualizadoEm"")
                VALUES
                    ('11111111-1111-1111-1111-000000000001', NULL, 'pix',          'EfiPix', 1, true, 'BRL', 'BR', NULL, NULL, NULL, now() at time zone 'utc', now() at time zone 'utc'),
                    ('11111111-1111-1111-1111-000000000002', NULL, 'manual',       'Manual', 99, true, 'BRL', 'BR', NULL, NULL, NULL, now() at time zone 'utc', now() at time zone 'utc'),
                    ('11111111-1111-1111-1111-000000000003', NULL, 'dinheiro',     'Manual', 99, true, 'BRL', 'BR', NULL, NULL, NULL, now() at time zone 'utc', now() at time zone 'utc'),
                    ('11111111-1111-1111-1111-000000000004', NULL, 'transferencia','Manual', 99, true, 'BRL', 'BR', NULL, NULL, NULL, now() at time zone 'utc', now() at time zone 'utc'),
                    ('11111111-1111-1111-1111-000000000005', NULL, 'cheque',       'Manual', 99, true, 'BRL', 'BR', NULL, NULL, NULL, now() at time zone 'utc', now() at time zone 'utc'),
                    ('11111111-1111-1111-1111-000000000006', NULL, 'outro',        'Manual', 99, true, 'BRL', 'BR', NULL, NULL, NULL, now() at time zone 'utc', now() at time zone 'utc')
                ON CONFLICT DO NOTHING;
            ");
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
                name: "gateway_health_snapshots");

            migrationBuilder.DropTable(
                name: "gateway_routing_rules");

            migrationBuilder.DropTable(
                name: "outbox_evento_integracao");

            migrationBuilder.DropTable(
                name: "pagamento_attempt_events");

            migrationBuilder.DropTable(
                name: "pagamento_attempts");

            migrationBuilder.DropIndex(
                name: "ix_fatura_pagamentos_empresa_status",
                table: "fatura_pagamentos");

            migrationBuilder.DropIndex(
                name: "IX_fatura_pagamentos_RegistradoPorUserId",
                table: "fatura_pagamentos");

            migrationBuilder.DropIndex(
                name: "ux_fatura_pagamentos_empresa_client_idempotency",
                table: "fatura_pagamentos");

            migrationBuilder.DropColumn(
                name: "ClientIdempotencyKey",
                table: "fatura_pagamentos");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "fatura_pagamentos");

            migrationBuilder.DropColumn(
                name: "TentativaAtualId",
                table: "fatura_pagamentos");

            migrationBuilder.DropColumn(
                name: "TotalTentativas",
                table: "fatura_pagamentos");

            migrationBuilder.DropColumn(
                name: "UltimaErrorCategory",
                table: "fatura_pagamentos");
        }
    }
}
