using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddLancamentoFinanceiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lancamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    DataEmissao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataVencimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: true),
                    FornecedorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Categoria = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    DocumentoReferencia = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    CanceladoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lancamentos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lancamento_baixas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LancamentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    DataBaixa = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContaBancariaId = table.Column<Guid>(type: "uuid", nullable: true),
                    MeioPagamento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ChaveExterna = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Observacao = table.Column<string>(type: "text", nullable: true),
                    RegistradoPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegistradoPorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EstornadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MotivoEstorno = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lancamento_baixas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lancamento_baixas_lancamentos_LancamentoId",
                        column: x => x.LancamentoId,
                        principalTable: "lancamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lancamento_baixas_EmpresaId_DataBaixa",
                table: "lancamento_baixas",
                columns: new[] { "EmpresaId", "DataBaixa" });

            migrationBuilder.CreateIndex(
                name: "IX_lancamento_baixas_LancamentoId_ChaveExterna",
                table: "lancamento_baixas",
                columns: new[] { "LancamentoId", "ChaveExterna" },
                unique: true,
                filter: "\"ChaveExterna\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_lancamentos_EmpresaId_DataVencimento",
                table: "lancamentos",
                columns: new[] { "EmpresaId", "DataVencimento" });

            migrationBuilder.CreateIndex(
                name: "IX_lancamentos_EmpresaId_Status",
                table: "lancamentos",
                columns: new[] { "EmpresaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_lancamentos_EmpresaId_Tipo",
                table: "lancamentos",
                columns: new[] { "EmpresaId", "Tipo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lancamento_baixas");

            migrationBuilder.DropTable(
                name: "lancamentos");
        }
    }
}
