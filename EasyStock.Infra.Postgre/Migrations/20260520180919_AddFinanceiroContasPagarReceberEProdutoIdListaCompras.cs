using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceiroContasPagarReceberEProdutoIdListaCompras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProdutoId",
                table: "itens_lista_compras",
                type: "uuid",
                nullable: true);

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
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

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

            migrationBuilder.AddColumn<bool>(
                name: "GerarContaPagarAutomaticaDePedidoFornecedor",
                table: "configuracoes_loja",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "GerarContaReceberAutomaticaDePedido",
                table: "configuracoes_loja",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StatusPedidoQueGeraContaReceber",
                table: "configuracoes_loja",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "categorias_financeiras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Profundidade = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Cor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Icone = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Ordem = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categorias_financeiras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_categorias_financeiras_categorias_financeiras_ParentId",
                        column: x => x.ParentId,
                        principalTable: "categorias_financeiras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_categorias_financeiras_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "centros_custo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_centros_custo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_centros_custo_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_centros_custo_lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "contas_pagar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaId = table.Column<Guid>(type: "uuid", nullable: true),
                    FornecedorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoriaFinanceiraId = table.Column<Guid>(type: "uuid", nullable: false),
                    CentroCustoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Observacoes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ValorTotal = table.Column<decimal>(type: "numeric(14,2)", nullable: false, defaultValue: 0m),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DataEmissao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataCompetencia = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Origem = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OrigemRefId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentoReferencia = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CanceladaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CanceladaPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contas_pagar", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contas_pagar_categorias_financeiras_CategoriaFinanceiraId",
                        column: x => x.CategoriaFinanceiraId,
                        principalTable: "categorias_financeiras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contas_pagar_centros_custo_CentroCustoId",
                        column: x => x.CentroCustoId,
                        principalTable: "centros_custo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_contas_pagar_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contas_pagar_fornecedores_FornecedorId",
                        column: x => x.FornecedorId,
                        principalTable: "fornecedores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_contas_pagar_lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "contas_receber",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoriaFinanceiraId = table.Column<Guid>(type: "uuid", nullable: false),
                    CentroCustoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Observacoes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ValorTotal = table.Column<decimal>(type: "numeric(14,2)", nullable: false, defaultValue: 0m),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DataEmissao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataCompetencia = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Origem = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OrigemRefId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentoReferencia = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    FaturaId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanceladaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CanceladaPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contas_receber", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contas_receber_categorias_financeiras_CategoriaFinanceiraId",
                        column: x => x.CategoriaFinanceiraId,
                        principalTable: "categorias_financeiras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contas_receber_centros_custo_CentroCustoId",
                        column: x => x.CentroCustoId,
                        principalTable: "centros_custo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_contas_receber_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_contas_receber_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contas_receber_faturas_FaturaId",
                        column: x => x.FaturaId,
                        principalTable: "faturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_contas_receber_lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "contas_pagar_alteracoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContaPagarId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlteradoPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlteradoPorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Campo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ValorAntigo = table.Column<string>(type: "text", nullable: true),
                    ValorNovo = table.Column<string>(type: "text", nullable: true),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contas_pagar_alteracoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contas_pagar_alteracoes_contas_pagar_ContaPagarId",
                        column: x => x.ContaPagarId,
                        principalTable: "contas_pagar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "parcelas_pagar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContaPagarId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    DataVencimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValorPago = table.Column<decimal>(type: "numeric(14,2)", nullable: false, defaultValue: 0m),
                    DataPagamentoTotal = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MetodoPlanejado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    NotificadaD3Em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotificadaD1Em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotificadaVencidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parcelas_pagar", x => x.Id);
                    table.ForeignKey(
                        name: "FK_parcelas_pagar_contas_pagar_ContaPagarId",
                        column: x => x.ContaPagarId,
                        principalTable: "contas_pagar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contas_financeiras_eventos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContaPagarId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContaReceberId = table.Column<Guid>(type: "uuid", nullable: true),
                    TipoEvento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ValorAntes = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ValorDepois = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    UsuarioNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contas_financeiras_eventos", x => x.Id);
                    table.CheckConstraint("ck_contas_financeiras_eventos_lado_xor", "(\"ContaPagarId\" IS NOT NULL AND \"ContaReceberId\" IS NULL) OR (\"ContaPagarId\" IS NULL AND \"ContaReceberId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_contas_financeiras_eventos_contas_pagar_ContaPagarId",
                        column: x => x.ContaPagarId,
                        principalTable: "contas_pagar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_contas_financeiras_eventos_contas_receber_ContaReceberId",
                        column: x => x.ContaReceberId,
                        principalTable: "contas_receber",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contas_receber_alteracoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContaReceberId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlteradoPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlteradoPorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Campo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ValorAntigo = table.Column<string>(type: "text", nullable: true),
                    ValorNovo = table.Column<string>(type: "text", nullable: true),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contas_receber_alteracoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contas_receber_alteracoes_contas_receber_ContaReceberId",
                        column: x => x.ContaReceberId,
                        principalTable: "contas_receber",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "parcelas_receber",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContaReceberId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    DataVencimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValorPago = table.Column<decimal>(type: "numeric(14,2)", nullable: false, defaultValue: 0m),
                    DataPagamentoTotal = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MetodoPlanejado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    EfiTxid = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PixCopiaCola = table.Column<string>(type: "text", nullable: true),
                    QrCodeBase64 = table.Column<string>(type: "text", nullable: true),
                    PixExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotificadaD3Em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotificadaD1Em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotificadaVencidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parcelas_receber", x => x.Id);
                    table.ForeignKey(
                        name: "FK_parcelas_receber_contas_receber_ContaReceberId",
                        column: x => x.ContaReceberId,
                        principalTable: "contas_receber",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pagamentos_parcela",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Lado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ParcelaPagarId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParcelaReceberId = table.Column<Guid>(type: "uuid", nullable: true),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Metodo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DataPagamento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GatewayProvedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    GatewayTransactionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DadosGatewayJson = table.Column<string>(type: "jsonb", nullable: true),
                    RegistradoPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegistradoPorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MovimentoCaixaId = table.Column<Guid>(type: "uuid", nullable: true),
                    EstornadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EstornadoPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    MotivoEstorno = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pagamentos_parcela", x => x.Id);
                    table.CheckConstraint("ck_pagamentos_parcela_lado_xor", "(\"ParcelaPagarId\" IS NOT NULL AND \"ParcelaReceberId\" IS NULL) OR (\"ParcelaPagarId\" IS NULL AND \"ParcelaReceberId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_pagamentos_parcela_movimentos_caixa_MovimentoCaixaId",
                        column: x => x.MovimentoCaixaId,
                        principalTable: "movimentos_caixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_pagamentos_parcela_parcelas_pagar_ParcelaPagarId",
                        column: x => x.ParcelaPagarId,
                        principalTable: "parcelas_pagar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pagamentos_parcela_parcelas_receber_ParcelaReceberId",
                        column: x => x.ParcelaReceberId,
                        principalTable: "parcelas_receber",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fatura_pagamentos_empresa_status",
                table: "fatura_pagamentos",
                columns: new[] { "EmpresaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ux_fatura_pagamentos_empresa_client_idempotency",
                table: "fatura_pagamentos",
                columns: new[] { "EmpresaId", "ClientIdempotencyKey" },
                unique: true,
                filter: "\"ClientIdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_categorias_financeiras_empresa_ativa",
                table: "categorias_financeiras",
                columns: new[] { "EmpresaId", "Ativa" });

            migrationBuilder.CreateIndex(
                name: "ix_categorias_financeiras_empresa_tipo",
                table: "categorias_financeiras",
                columns: new[] { "EmpresaId", "Tipo" });

            migrationBuilder.CreateIndex(
                name: "ix_categorias_financeiras_parent",
                table: "categorias_financeiras",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "ux_categorias_financeiras_empresa_parent_nome",
                table: "categorias_financeiras",
                columns: new[] { "EmpresaId", "ParentId", "Nome" },
                unique: true,
                filter: "\"Ativa\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "ix_centros_custo_empresa_ativo",
                table: "centros_custo",
                columns: new[] { "EmpresaId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "ix_centros_custo_empresa_loja",
                table: "centros_custo",
                columns: new[] { "EmpresaId", "LojaId" });

            migrationBuilder.CreateIndex(
                name: "IX_centros_custo_LojaId",
                table: "centros_custo",
                column: "LojaId");

            migrationBuilder.CreateIndex(
                name: "ux_centros_custo_empresa_codigo",
                table: "centros_custo",
                columns: new[] { "EmpresaId", "Codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contas_fin_eventos_cp_data",
                table: "contas_financeiras_eventos",
                columns: new[] { "ContaPagarId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "ix_contas_fin_eventos_cr_data",
                table: "contas_financeiras_eventos",
                columns: new[] { "ContaReceberId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "ix_contas_fin_eventos_empresa_tipo_data",
                table: "contas_financeiras_eventos",
                columns: new[] { "EmpresaId", "TipoEvento", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_contas_pagar_CategoriaFinanceiraId",
                table: "contas_pagar",
                column: "CategoriaFinanceiraId");

            migrationBuilder.CreateIndex(
                name: "IX_contas_pagar_CentroCustoId",
                table: "contas_pagar",
                column: "CentroCustoId");

            migrationBuilder.CreateIndex(
                name: "ix_contas_pagar_empresa_categoria",
                table: "contas_pagar",
                columns: new[] { "EmpresaId", "CategoriaFinanceiraId" });

            migrationBuilder.CreateIndex(
                name: "ix_contas_pagar_empresa_centro",
                table: "contas_pagar",
                columns: new[] { "EmpresaId", "CentroCustoId" });

            migrationBuilder.CreateIndex(
                name: "ix_contas_pagar_empresa_emissao",
                table: "contas_pagar",
                columns: new[] { "EmpresaId", "DataEmissao" });

            migrationBuilder.CreateIndex(
                name: "ix_contas_pagar_empresa_fornecedor",
                table: "contas_pagar",
                columns: new[] { "EmpresaId", "FornecedorId" });

            migrationBuilder.CreateIndex(
                name: "ix_contas_pagar_empresa_status",
                table: "contas_pagar",
                columns: new[] { "EmpresaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_contas_pagar_FornecedorId",
                table: "contas_pagar",
                column: "FornecedorId");

            migrationBuilder.CreateIndex(
                name: "IX_contas_pagar_LojaId",
                table: "contas_pagar",
                column: "LojaId");

            migrationBuilder.CreateIndex(
                name: "ux_contas_pagar_empresa_documento_ref",
                table: "contas_pagar",
                columns: new[] { "EmpresaId", "DocumentoReferencia" },
                unique: true,
                filter: "\"DocumentoReferencia\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_contas_pagar_empresa_origem_ref",
                table: "contas_pagar",
                columns: new[] { "EmpresaId", "Origem", "OrigemRefId" },
                unique: true,
                filter: "\"OrigemRefId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_contas_pagar_alteracoes_conta_data",
                table: "contas_pagar_alteracoes",
                columns: new[] { "ContaPagarId", "AlteradoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_contas_receber_CategoriaFinanceiraId",
                table: "contas_receber",
                column: "CategoriaFinanceiraId");

            migrationBuilder.CreateIndex(
                name: "IX_contas_receber_CentroCustoId",
                table: "contas_receber",
                column: "CentroCustoId");

            migrationBuilder.CreateIndex(
                name: "IX_contas_receber_ClienteId",
                table: "contas_receber",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "ix_contas_receber_empresa_categoria",
                table: "contas_receber",
                columns: new[] { "EmpresaId", "CategoriaFinanceiraId" });

            migrationBuilder.CreateIndex(
                name: "ix_contas_receber_empresa_centro",
                table: "contas_receber",
                columns: new[] { "EmpresaId", "CentroCustoId" });

            migrationBuilder.CreateIndex(
                name: "ix_contas_receber_empresa_cliente",
                table: "contas_receber",
                columns: new[] { "EmpresaId", "ClienteId" });

            migrationBuilder.CreateIndex(
                name: "ix_contas_receber_empresa_emissao",
                table: "contas_receber",
                columns: new[] { "EmpresaId", "DataEmissao" });

            migrationBuilder.CreateIndex(
                name: "ix_contas_receber_empresa_status",
                table: "contas_receber",
                columns: new[] { "EmpresaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_contas_receber_FaturaId",
                table: "contas_receber",
                column: "FaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_contas_receber_LojaId",
                table: "contas_receber",
                column: "LojaId");

            migrationBuilder.CreateIndex(
                name: "ux_contas_receber_empresa_documento_ref",
                table: "contas_receber",
                columns: new[] { "EmpresaId", "DocumentoReferencia" },
                unique: true,
                filter: "\"DocumentoReferencia\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_contas_receber_empresa_origem_ref",
                table: "contas_receber",
                columns: new[] { "EmpresaId", "Origem", "OrigemRefId" },
                unique: true,
                filter: "\"OrigemRefId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_contas_receber_alteracoes_conta_data",
                table: "contas_receber_alteracoes",
                columns: new[] { "ContaReceberId", "AlteradoEm" });

            migrationBuilder.CreateIndex(
                name: "ix_pagamentos_parcela_empresa_data",
                table: "pagamentos_parcela",
                columns: new[] { "EmpresaId", "DataPagamento" });

            migrationBuilder.CreateIndex(
                name: "ix_pagamentos_parcela_empresa_lado_status",
                table: "pagamentos_parcela",
                columns: new[] { "EmpresaId", "Lado", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_pagamentos_parcela_movimento_caixa",
                table: "pagamentos_parcela",
                column: "MovimentoCaixaId");

            migrationBuilder.CreateIndex(
                name: "IX_pagamentos_parcela_ParcelaPagarId",
                table: "pagamentos_parcela",
                column: "ParcelaPagarId");

            migrationBuilder.CreateIndex(
                name: "IX_pagamentos_parcela_ParcelaReceberId",
                table: "pagamentos_parcela",
                column: "ParcelaReceberId");

            migrationBuilder.CreateIndex(
                name: "ux_pagamentos_parcela_gateway_tx",
                table: "pagamentos_parcela",
                columns: new[] { "GatewayProvedor", "GatewayTransactionId" },
                unique: true,
                filter: "\"GatewayTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_parcelas_pagar_empresa_status",
                table: "parcelas_pagar",
                columns: new[] { "EmpresaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_parcelas_pagar_empresa_vencimento_status",
                table: "parcelas_pagar",
                columns: new[] { "EmpresaId", "DataVencimento", "Status" });

            migrationBuilder.CreateIndex(
                name: "ux_parcelas_pagar_conta_numero",
                table: "parcelas_pagar",
                columns: new[] { "ContaPagarId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parcelas_receber_empresa_status",
                table: "parcelas_receber",
                columns: new[] { "EmpresaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_parcelas_receber_empresa_vencimento_status",
                table: "parcelas_receber",
                columns: new[] { "EmpresaId", "DataVencimento", "Status" });

            migrationBuilder.CreateIndex(
                name: "ux_parcelas_receber_conta_numero",
                table: "parcelas_receber",
                columns: new[] { "ContaReceberId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_parcelas_receber_efi_txid",
                table: "parcelas_receber",
                column: "EfiTxid",
                unique: true,
                filter: "\"EfiTxid\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contas_financeiras_eventos");

            migrationBuilder.DropTable(
                name: "contas_pagar_alteracoes");

            migrationBuilder.DropTable(
                name: "contas_receber_alteracoes");

            migrationBuilder.DropTable(
                name: "pagamentos_parcela");

            migrationBuilder.DropTable(
                name: "parcelas_pagar");

            migrationBuilder.DropTable(
                name: "parcelas_receber");

            migrationBuilder.DropTable(
                name: "contas_pagar");

            migrationBuilder.DropTable(
                name: "contas_receber");

            migrationBuilder.DropTable(
                name: "categorias_financeiras");

            migrationBuilder.DropTable(
                name: "centros_custo");

            migrationBuilder.DropIndex(
                name: "ix_fatura_pagamentos_empresa_status",
                table: "fatura_pagamentos");

            migrationBuilder.DropIndex(
                name: "ux_fatura_pagamentos_empresa_client_idempotency",
                table: "fatura_pagamentos");

            migrationBuilder.DropColumn(
                name: "ProdutoId",
                table: "itens_lista_compras");

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

            migrationBuilder.DropColumn(
                name: "GerarContaPagarAutomaticaDePedidoFornecedor",
                table: "configuracoes_loja");

            migrationBuilder.DropColumn(
                name: "GerarContaReceberAutomaticaDePedido",
                table: "configuracoes_loja");

            migrationBuilder.DropColumn(
                name: "StatusPedidoQueGeraContaReceber",
                table: "configuracoes_loja");
        }
    }
}
