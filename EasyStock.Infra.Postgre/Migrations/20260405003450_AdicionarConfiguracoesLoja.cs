using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarConfiguracoesLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "configuracoes_loja",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiasAlertaValidade = table.Column<int>(type: "integer", nullable: false, defaultValue: 15),
                    DiasAlertaParado = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    QuantidadeMinimaPadrao = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    NotificarEstoqueCritico = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NotificarValidade = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NotificarParado = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NotificarReposicao = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    FifoAtivo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Moeda = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "BRL"),
                    Timezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "America/Sao_Paulo"),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuracoes_loja", x => x.Id);
                    table.ForeignKey(
                        name: "FK_configuracoes_loja_lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_configuracoes_loja_LojaId",
                table: "configuracoes_loja",
                column: "LojaId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuracoes_loja");
        }
    }
}
