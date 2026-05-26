using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddStorefrontAuthFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TelefoneHash",
                table: "clientes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                table: "cliente_session",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_clientes_empresa_telefone_hash",
                table: "clientes",
                columns: new[] { "EmpresaId", "TelefoneHash" },
                unique: true,
                filter: "\"TelefoneHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_clientes_empresa_telefone_hash",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "TelefoneHash",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                table: "cliente_session");
        }
    }
}
