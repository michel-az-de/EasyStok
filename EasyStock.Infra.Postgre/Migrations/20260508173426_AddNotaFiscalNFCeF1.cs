using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddNotaFiscalNFCeF1 : Migration
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
                name: "nota_fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    venda_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modelo = table.Column<short>(type: "smallint", nullable: false),
                    serie = table.Column<int>(type: "integer", nullable: false),
                    n_nf = table.Column<int>(type: "integer", nullable: false),
                    chave_acesso = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tp_emis = table.Column<short>(type: "smallint", nullable: false),
                    tp_amb = table.Column<short>(type: "smallint", nullable: false),
                    dh_emi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    dh_autorizacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    dh_cancelamento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    protocolo_autorizacao = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    protocolo_cancelamento = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    xml_autorizado = table.Column<string>(type: "text", nullable: true),
                    xml_assinado_local = table.Column<string>(type: "text", nullable: true),
                    xml_evento_cancelamento = table.Column<string>(type: "text", nullable: true),
                    cod_rejeicao = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    motivo_rejeicao = table.Column<string>(type: "text", nullable: true),
                    justificativa_cancelamento = table.Column<string>(type: "text", nullable: true),
                    cliente_cpf_cnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    valor_total = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    forma_pagamento_principal = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    arquivado = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nota_fiscal", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "nota_fiscal_certificado_a1",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfx_cifrado = table.Column<byte[]>(type: "bytea", nullable: false),
                    senha_cifrada = table.Column<byte[]>(type: "bytea", nullable: false),
                    iv = table.Column<byte[]>(type: "bytea", nullable: false),
                    tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    kek_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nome_titular = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    documento_titular = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    valido_de = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valido_ate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    criado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nota_fiscal_certificado_a1", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "nota_fiscal_contador",
                columns: table => new
                {
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modelo = table.Column<short>(type: "smallint", nullable: false),
                    serie = table.Column<int>(type: "integer", nullable: false),
                    ultimo_numero = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nota_fiscal_contador", x => new { x.empresa_id, x.loja_id, x.modelo, x.serie });
                });

            migrationBuilder.CreateTable(
                name: "nota_fiscal_inutilizacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modelo = table.Column<short>(type: "smallint", nullable: false),
                    serie = table.Column<int>(type: "integer", nullable: false),
                    numero_inicial = table.Column<int>(type: "integer", nullable: false),
                    numero_final = table.Column<int>(type: "integer", nullable: false),
                    ano = table.Column<int>(type: "integer", nullable: false),
                    justificativa = table.Column<string>(type: "text", nullable: false),
                    protocolo_inutilizacao = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xml_inutilizacao = table.Column<string>(type: "text", nullable: true),
                    motivo_rejeicao = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nota_fiscal_inutilizacao", x => x.id);
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
                name: "nota_fiscal_evento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nota_fiscal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    xml_payload = table.Column<string>(type: "text", nullable: true),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ocorrido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nota_fiscal_evento", x => x.id);
                    table.ForeignKey(
                        name: "FK_nota_fiscal_evento_nota_fiscal_nota_fiscal_id",
                        column: x => x.nota_fiscal_id,
                        principalTable: "nota_fiscal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "nota_fiscal_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nota_fiscal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    produto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    descricao_snapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    codigo_produto = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ean = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    ncm = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    cfop = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    cest = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    unidade_comercial = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    quantidade = table.Column<decimal>(type: "numeric(15,4)", nullable: false),
                    preco_unitario = table.Column<decimal>(type: "numeric(15,4)", nullable: false),
                    desconto = table.Column<decimal>(type: "numeric(14,2)", nullable: false, defaultValue: 0m),
                    subtotal = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    origem_mercadoria = table.Column<short>(type: "smallint", nullable: false),
                    cst_csosn = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    icms_modalidade_bc = table.Column<int>(type: "integer", nullable: true),
                    icms_aliquota = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    icms_valor = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    cst_pis = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    pis_aliquota = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    pis_valor = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    cst_cofins = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    cofins_aliquota = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    cofins_valor = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    ibs_cst = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ibs_aliquota = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    ibs_valor = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    cbs_cst = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    cbs_aliquota = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    cbs_valor = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    is_cst = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    is_aliquota = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    is_valor = table.Column<decimal>(type: "numeric(14,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nota_fiscal_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_nota_fiscal_item_nota_fiscal_nota_fiscal_id",
                        column: x => x.nota_fiscal_id,
                        principalTable: "nota_fiscal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "nota_fiscal_pagamento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nota_fiscal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    forma_pagamento_codigo = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    bandeira_cartao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    cnpj_credenciadora = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    nsu = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    troco = table.Column<decimal>(type: "numeric(14,2)", nullable: false, defaultValue: 0m)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nota_fiscal_pagamento", x => x.id);
                    table.ForeignKey(
                        name: "FK_nota_fiscal_pagamento_nota_fiscal_nota_fiscal_id",
                        column: x => x.nota_fiscal_id,
                        principalTable: "nota_fiscal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "ix_nota_fiscal_chave",
                table: "nota_fiscal",
                column: "chave_acesso");

            migrationBuilder.CreateIndex(
                name: "ix_nota_fiscal_status_dh",
                table: "nota_fiscal",
                columns: new[] { "empresa_id", "status", "dh_emi" });

            migrationBuilder.CreateIndex(
                name: "ux_nota_fiscal_empresa_loja_modelo_serie_nnf",
                table: "nota_fiscal",
                columns: new[] { "empresa_id", "loja_id", "modelo", "serie", "n_nf" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_nota_fiscal_idempotency",
                table: "nota_fiscal",
                columns: new[] { "empresa_id", "loja_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nota_fiscal_cert_empresa_ativo",
                table: "nota_fiscal_certificado_a1",
                columns: new[] { "empresa_id", "ativo" },
                filter: "ativo = true");

            migrationBuilder.CreateIndex(
                name: "ix_nota_fiscal_evento_nf",
                table: "nota_fiscal_evento",
                columns: new[] { "nota_fiscal_id", "ocorrido_em" });

            migrationBuilder.CreateIndex(
                name: "ix_nota_fiscal_inut_loja_serie_ano",
                table: "nota_fiscal_inutilizacao",
                columns: new[] { "empresa_id", "loja_id", "modelo", "serie", "ano" });

            migrationBuilder.CreateIndex(
                name: "ix_nota_fiscal_item_nf",
                table: "nota_fiscal_item",
                column: "nota_fiscal_id");

            migrationBuilder.CreateIndex(
                name: "ix_nota_fiscal_pagamento_nf",
                table: "nota_fiscal_pagamento",
                column: "nota_fiscal_id");

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
                name: "nota_fiscal_certificado_a1");

            migrationBuilder.DropTable(
                name: "nota_fiscal_contador");

            migrationBuilder.DropTable(
                name: "nota_fiscal_evento");

            migrationBuilder.DropTable(
                name: "nota_fiscal_inutilizacao");

            migrationBuilder.DropTable(
                name: "nota_fiscal_item");

            migrationBuilder.DropTable(
                name: "nota_fiscal_pagamento");

            migrationBuilder.DropTable(
                name: "outbox_evento_integracao");

            migrationBuilder.DropTable(
                name: "nota_fiscal");

            migrationBuilder.DropIndex(
                name: "IX_fatura_pagamentos_RegistradoPorUserId",
                table: "fatura_pagamentos");
        }
    }
}
