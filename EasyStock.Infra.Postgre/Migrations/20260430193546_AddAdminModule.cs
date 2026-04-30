using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Severidade",
                table: "notificacoes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Media");

            migrationBuilder.CreateTable(
                name: "admin_impersonation_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminUsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    InicioEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FimEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Ip = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_impersonation_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_admin_impersonation_logs_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_admin_impersonation_logs_usuarios_AdminUsuarioId",
                        column: x => x.AdminUsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "admin_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Categoria = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Prioridade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AtendenteId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_admin_tickets_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_admin_tickets_usuarios_AtendenteId",
                        column: x => x.AtendenteId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Apt = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Endereco = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Telefone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Documento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    OrderCount = table.Column<int>(type: "integer", nullable: false),
                    LastOrderAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clientes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_clientes_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fechamentos_caixa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    SaldoInicial = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    TotalVendas = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    TotalPagamentosPedidos = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    TotalEntradasExtras = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    TotalSaidasExtras = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    SaldoFinal = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    FechadoPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FechadoPorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    FechadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fechamentos_caixa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fechamentos_caixa_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fechamentos_caixa_lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "fornecedor_alteracoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FornecedorId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_fornecedor_alteracoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fornecedor_alteracoes_fornecedores_FornecedorId",
                        column: x => x.FornecedorId,
                        principalTable: "fornecedores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listas_compras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    CriadaPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadaPorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArquivadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_listas_compras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_listas_compras_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_listas_compras_lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "lotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DataProducao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OperadorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperadorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    FotoUrl = table.Column<string>(type: "text", nullable: true),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    MobileBatchId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinalizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lotes_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lotes_lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "mobile_batches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    batch_photo = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_operator_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    lote = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: true),
                    erp_lote_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_batches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mobile_cash_entries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_operator_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: true),
                    erp_movimento_caixa_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_cash_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mobile_clients",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Apt = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    last_order = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    order_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_operator_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: true),
                    erp_cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mobile_device_backups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_json = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    bundle_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    operator_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Note = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_device_backups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mobile_device_commands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    executed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_device_commands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mobile_devices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    api_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paired_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    default_operator_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pairing_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    pairing_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    paired_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_seen_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    revoked = table.Column<bool>(type: "boolean", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mobile_orders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    client_snapshot_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    client_snapshot_ref = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Total = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_operator_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    history = table.Column<string>(type: "jsonb", nullable: true),
                    confirmed_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    fact_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: true),
                    erp_venda_id = table.Column<Guid>(type: "uuid", nullable: true),
                    erp_pedido_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mobile_products",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Price = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Stock = table.Column<int>(type: "integer", nullable: false),
                    is_custom = table.Column<bool>(type: "boolean", nullable: false),
                    is_approved = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_operator_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    sku = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    default_weight_g = table.Column<int>(type: "integer", nullable: true),
                    default_validity_days = table.Column<int>(type: "integer", nullable: true),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    loja_id = table.Column<Guid>(type: "uuid", nullable: true),
                    erp_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "movimentos_caixa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: true),
                    Metodo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Categoria = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Referencia = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DataMovimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RegistradoPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegistradoPorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    EstornadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EstornadoPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EstornadoPorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    MotivoEstorno = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimentos_caixa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_movimentos_caixa_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_movimentos_caixa_lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "venda_alteracoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendaId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_venda_alteracoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_venda_alteracoes_vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "admin_ticket_mensagens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    AutorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Conteudo = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_ticket_mensagens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_admin_ticket_mensagens_admin_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "admin_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_admin_ticket_mensagens_usuarios_AutorId",
                        column: x => x.AutorId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cliente_alteracoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_cliente_alteracoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cliente_alteracoes_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cliente_documentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Valor = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Emissor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    EmitidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidoAte = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Principal = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cliente_documentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cliente_documentos_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cliente_enderecos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Logradouro = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Numero = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Complemento = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Bairro = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Cidade = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Estado = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Cep = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Pais = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Referencia = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Padrao = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cliente_enderecos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cliente_enderecos_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cliente_telefones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Numero = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Whatsapp = table.Column<bool>(type: "boolean", nullable: false),
                    Principal = table.Column<bool>(type: "boolean", nullable: false),
                    Observacao = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cliente_telefones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cliente_telefones_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pedidos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClienteNome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ClienteApt = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ClienteTelefone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    MobileOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    VendaId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EntreguEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CanceladoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedidos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pedidos_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_pedidos_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedidos_lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_pedidos_vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "itens_lista_compras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ListaComprasId = table.Column<Guid>(type: "uuid", nullable: false),
                    Texto = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Quantidade = table.Column<decimal>(type: "numeric(14,3)", nullable: true),
                    Unidade = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Observacao = table.Column<string>(type: "text", nullable: true),
                    Categoria = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Done = table.Column<bool>(type: "boolean", nullable: false),
                    DoneEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DonePorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DonePorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_itens_lista_compras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_itens_lista_compras_listas_compras_ListaComprasId",
                        column: x => x.ListaComprasId,
                        principalTable: "listas_compras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lote_itens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Unidade = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Quantidade = table.Column<int>(type: "integer", nullable: false),
                    PesoG = table.Column<int>(type: "integer", nullable: true),
                    ValidadeDias = table.Column<int>(type: "integer", nullable: true),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FotoUrl = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lote_itens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lote_itens_lotes_LoteId",
                        column: x => x.LoteId,
                        principalTable: "lotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lote_itens_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "mobile_batch_items",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    batch_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    product_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Qty = table.Column<int>(type: "integer", nullable: false),
                    Photo = table.Column<string>(type: "text", nullable: true),
                    weight_g = table.Column<int>(type: "integer", nullable: true),
                    validity_days = table.Column<int>(type: "integer", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_batch_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_batch_items_mobile_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "mobile_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mobile_order_items",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    product_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Qty = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(10,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_order_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_order_items_mobile_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "mobile_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pedido_eventos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StatusAntigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    StatusNovo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Detalhes = table.Column<string>(type: "text", nullable: true),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    UsuarioNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OcorridoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedido_eventos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pedido_eventos_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pedido_itens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Unidade = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Quantidade = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    PrecoUnitario = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Observacao = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedido_itens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pedido_itens_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pedido_itens_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "pedido_pagamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Metodo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Referencia = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Observacao = table.Column<string>(type: "text", nullable: true),
                    PagoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RegistradoPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegistradoPorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedido_pagamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pedido_pagamentos_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lote_etiquetas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoteItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequencial = table.Column<int>(type: "integer", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConferidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConferidaPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConferidaPorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ObservacaoConferencia = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lote_etiquetas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lote_etiquetas_lote_itens_LoteItemId",
                        column: x => x.LoteItemId,
                        principalTable: "lote_itens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lote_etiquetas_lotes_LoteId",
                        column: x => x.LoteId,
                        principalTable: "lotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_impersonation_logs_AdminUsuarioId",
                table: "admin_impersonation_logs",
                column: "AdminUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_admin_impersonation_logs_EmpresaId",
                table: "admin_impersonation_logs",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_admin_ticket_mensagens_AutorId",
                table: "admin_ticket_mensagens",
                column: "AutorId");

            migrationBuilder.CreateIndex(
                name: "IX_admin_ticket_mensagens_TicketId",
                table: "admin_ticket_mensagens",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_admin_tickets_AtendenteId",
                table: "admin_tickets",
                column: "AtendenteId");

            migrationBuilder.CreateIndex(
                name: "IX_admin_tickets_EmpresaId",
                table: "admin_tickets",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_cliente_alteracoes_ClienteId_AlteradoEm",
                table: "cliente_alteracoes",
                columns: new[] { "ClienteId", "AlteradoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_cliente_documentos_ClienteId",
                table: "cliente_documentos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_cliente_enderecos_ClienteId",
                table: "cliente_enderecos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_cliente_telefones_ClienteId",
                table: "cliente_telefones",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_EmpresaId",
                table: "clientes",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_fechamentos_caixa_EmpresaId_LojaId_Data",
                table: "fechamentos_caixa",
                columns: new[] { "EmpresaId", "LojaId", "Data" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fechamentos_caixa_LojaId",
                table: "fechamentos_caixa",
                column: "LojaId");

            migrationBuilder.CreateIndex(
                name: "IX_fornecedor_alteracoes_FornecedorId_AlteradoEm",
                table: "fornecedor_alteracoes",
                columns: new[] { "FornecedorId", "AlteradoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_itens_lista_compras_ListaComprasId",
                table: "itens_lista_compras",
                column: "ListaComprasId");

            migrationBuilder.CreateIndex(
                name: "IX_listas_compras_EmpresaId",
                table: "listas_compras",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_listas_compras_LojaId",
                table: "listas_compras",
                column: "LojaId");

            migrationBuilder.CreateIndex(
                name: "IX_lote_etiquetas_Codigo",
                table: "lote_etiquetas",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lote_etiquetas_LoteId_Status",
                table: "lote_etiquetas",
                columns: new[] { "LoteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_lote_etiquetas_LoteItemId",
                table: "lote_etiquetas",
                column: "LoteItemId");

            migrationBuilder.CreateIndex(
                name: "IX_lote_itens_LoteId",
                table: "lote_itens",
                column: "LoteId");

            migrationBuilder.CreateIndex(
                name: "IX_lote_itens_ProdutoId",
                table: "lote_itens",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_lotes_EmpresaId_Codigo",
                table: "lotes",
                columns: new[] { "EmpresaId", "Codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lotes_LojaId",
                table: "lotes",
                column: "LojaId");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_batch_items_batch_id",
                table: "mobile_batch_items",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_mobile_device_backups_device",
                table: "mobile_device_backups",
                columns: new[] { "device_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mobile_device_backups_empresa",
                table: "mobile_device_backups",
                columns: new[] { "empresa_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mobile_device_commands_empresa",
                table: "mobile_device_commands",
                columns: new[] { "empresa_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mobile_device_commands_pending",
                table: "mobile_device_commands",
                columns: new[] { "device_id", "delivered_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mobile_devices_api_key",
                table: "mobile_devices",
                column: "api_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mobile_devices_empresa_id",
                table: "mobile_devices",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_mobile_devices_pairing_code",
                table: "mobile_devices",
                column: "pairing_code");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_order_items_order_id",
                table: "mobile_order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_movimentos_caixa_EmpresaId",
                table: "movimentos_caixa",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_movimentos_caixa_LojaId",
                table: "movimentos_caixa",
                column: "LojaId");

            migrationBuilder.CreateIndex(
                name: "IX_pedido_eventos_PedidoId",
                table: "pedido_eventos",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_pedido_itens_PedidoId",
                table: "pedido_itens",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_pedido_itens_ProdutoId",
                table: "pedido_itens",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_pedido_pagamentos_PedidoId",
                table: "pedido_pagamentos",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_ClienteId",
                table: "pedidos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_EmpresaId",
                table: "pedidos",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_LojaId",
                table: "pedidos",
                column: "LojaId");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_VendaId",
                table: "pedidos",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_venda_alteracoes_VendaId_AlteradoEm",
                table: "venda_alteracoes",
                columns: new[] { "VendaId", "AlteradoEm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_impersonation_logs");

            migrationBuilder.DropTable(
                name: "admin_ticket_mensagens");

            migrationBuilder.DropTable(
                name: "cliente_alteracoes");

            migrationBuilder.DropTable(
                name: "cliente_documentos");

            migrationBuilder.DropTable(
                name: "cliente_enderecos");

            migrationBuilder.DropTable(
                name: "cliente_telefones");

            migrationBuilder.DropTable(
                name: "fechamentos_caixa");

            migrationBuilder.DropTable(
                name: "fornecedor_alteracoes");

            migrationBuilder.DropTable(
                name: "itens_lista_compras");

            migrationBuilder.DropTable(
                name: "lote_etiquetas");

            migrationBuilder.DropTable(
                name: "mobile_batch_items");

            migrationBuilder.DropTable(
                name: "mobile_cash_entries");

            migrationBuilder.DropTable(
                name: "mobile_clients");

            migrationBuilder.DropTable(
                name: "mobile_device_backups");

            migrationBuilder.DropTable(
                name: "mobile_device_commands");

            migrationBuilder.DropTable(
                name: "mobile_devices");

            migrationBuilder.DropTable(
                name: "mobile_order_items");

            migrationBuilder.DropTable(
                name: "mobile_products");

            migrationBuilder.DropTable(
                name: "movimentos_caixa");

            migrationBuilder.DropTable(
                name: "pedido_eventos");

            migrationBuilder.DropTable(
                name: "pedido_itens");

            migrationBuilder.DropTable(
                name: "pedido_pagamentos");

            migrationBuilder.DropTable(
                name: "venda_alteracoes");

            migrationBuilder.DropTable(
                name: "admin_tickets");

            migrationBuilder.DropTable(
                name: "listas_compras");

            migrationBuilder.DropTable(
                name: "lote_itens");

            migrationBuilder.DropTable(
                name: "mobile_batches");

            migrationBuilder.DropTable(
                name: "mobile_orders");

            migrationBuilder.DropTable(
                name: "pedidos");

            migrationBuilder.DropTable(
                name: "lotes");

            migrationBuilder.DropTable(
                name: "clientes");

            migrationBuilder.AlterColumn<string>(
                name: "Severidade",
                table: "notificacoes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Media",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }
    }
}
