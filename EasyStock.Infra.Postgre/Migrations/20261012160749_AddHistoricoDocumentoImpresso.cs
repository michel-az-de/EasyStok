using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoricoDocumentoImpresso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Codigo",
                table: "impressoes_pendentes",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmadaPor",
                table: "impressoes_pendentes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Documento",
                table: "impressoes_pendentes",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hash",
                table: "impressoes_pendentes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Html",
                table: "impressoes_pendentes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Intencao",
                table: "impressoes_pendentes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Modelo",
                table: "impressoes_pendentes",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalId",
                table: "impressoes_pendentes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Perfil",
                table: "impressoes_pendentes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SolicitacaoId",
                table: "impressoes_pendentes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UsuarioId",
                table: "impressoes_pendentes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Versao",
                table: "impressoes_pendentes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_impressoes_empresa_solicitacao",
                table: "impressoes_pendentes",
                columns: new[] { "EmpresaId", "SolicitacaoId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_impressoes_empresa_solicitacao",
                table: "impressoes_pendentes");

            migrationBuilder.DropColumn(
                name: "Codigo",
                table: "impressoes_pendentes");

            migrationBuilder.DropColumn(
                name: "ConfirmadaPor",
                table: "impressoes_pendentes");

            migrationBuilder.DropColumn(
                name: "Documento",
                table: "impressoes_pendentes");

            migrationBuilder.DropColumn(
                name: "Hash",
                table: "impressoes_pendentes");

            migrationBuilder.DropColumn(
                name: "Html",
                table: "impressoes_pendentes");

            migrationBuilder.DropColumn(
                name: "Intencao",
                table: "impressoes_pendentes");

            migrationBuilder.DropColumn(
                name: "Modelo",
                table: "impressoes_pendentes");

            migrationBuilder.DropColumn(
                name: "OriginalId",
                table: "impressoes_pendentes");

            migrationBuilder.DropColumn(
                name: "Perfil",
                table: "impressoes_pendentes");

            migrationBuilder.DropColumn(
                name: "SolicitacaoId",
                table: "impressoes_pendentes");

            migrationBuilder.DropColumn(
                name: "UsuarioId",
                table: "impressoes_pendentes");

            migrationBuilder.DropColumn(
                name: "Versao",
                table: "impressoes_pendentes");
        }
    }
}
