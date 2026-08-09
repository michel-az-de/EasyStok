using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddClientePessoaJuridica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InscricaoEstadual",
                table: "clientes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NomeFantasia",
                table: "clientes",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TipoPessoa",
                table: "clientes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "fisica");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clientes_tipo_pessoa",
                table: "clientes",
                sql: "\"TipoPessoa\" IN ('fisica','juridica')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_clientes_tipo_pessoa",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "InscricaoEstadual",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "NomeFantasia",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "TipoPessoa",
                table: "clientes");
        }
    }
}
