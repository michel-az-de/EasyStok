using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class CadastrosR4EVinculoCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClienteAnteriorId",
                table: "atendimento_conversas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HistoricoPedidosVerificado",
                table: "atendimento_conversas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid[]>(
                name: "PedidosHistoricoIds",
                table: "atendimento_conversas",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<bool>(
                name: "VinculoEmRevisao",
                table: "atendimento_conversas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "VinculoRevisao",
                table: "atendimento_conversas",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "alteracoes_vinculo_conversa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversaId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteAnteriorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClienteNovoId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevisaoAnterior = table.Column<long>(type: "bigint", nullable: false),
                    Revisao = table.Column<long>(type: "bigint", nullable: false),
                    Acao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alteracoes_vinculo_conversa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alteracoes_vinculo_conversa_atendimento_conversas_ConversaId",
                        column: x => x.ConversaId,
                        principalTable: "atendimento_conversas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contas_bancarias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Instituicao = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Agencia = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Conta = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Titular = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    Revisao = table.Column<long>(type: "bigint", nullable: false),
                    UltimaOperacaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlteradoPor = table.Column<Guid>(type: "uuid", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contas_bancarias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contas_bancarias_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metodos_pagamento_empresa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    Revisao = table.Column<long>(type: "bigint", nullable: false),
                    UltimaOperacaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlteradoPor = table.Column<Guid>(type: "uuid", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metodos_pagamento_empresa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metodos_pagamento_empresa_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alteracoes_vinculo_conversa_ConversaId",
                table: "alteracoes_vinculo_conversa",
                column: "ConversaId");

            migrationBuilder.CreateIndex(
                name: "IX_alteracoes_vinculo_conversa_EmpresaId_ConversaId_RequestId",
                table: "alteracoes_vinculo_conversa",
                columns: new[] { "EmpresaId", "ConversaId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alteracoes_vinculo_conversa_EmpresaId_ConversaId_Revisao",
                table: "alteracoes_vinculo_conversa",
                columns: new[] { "EmpresaId", "ConversaId", "Revisao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contas_bancarias_EmpresaId_Ativo",
                table: "contas_bancarias",
                columns: new[] { "EmpresaId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_metodos_pagamento_empresa_EmpresaId_Codigo",
                table: "metodos_pagamento_empresa",
                columns: new[] { "EmpresaId", "Codigo" },
                unique: true);

            foreach (var tabela in new[] { "alteracoes_vinculo_conversa", "contas_bancarias", "metodos_pagamento_empresa" })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {tabela} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {tabela} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON {tabela}
                        USING (current_setting('app.bypass_rls', true) = 'true'
                            OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid)
                        WITH CHECK (current_setting('app.bypass_rls', true) = 'true'
                            OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid);
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alteracoes_vinculo_conversa");

            migrationBuilder.DropTable(
                name: "contas_bancarias");

            migrationBuilder.DropTable(
                name: "metodos_pagamento_empresa");

            migrationBuilder.DropColumn(
                name: "ClienteAnteriorId",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "HistoricoPedidosVerificado",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "PedidosHistoricoIds",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "VinculoEmRevisao",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "VinculoRevisao",
                table: "atendimento_conversas");
        }
    }
}
