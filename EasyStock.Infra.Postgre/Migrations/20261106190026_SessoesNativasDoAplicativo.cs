using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class SessoesNativasDoAplicativo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SessaoNativaId",
                table: "refresh_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "sessao_nativa_id",
                table: "mobile_devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sessoes_nativas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    DispositivoId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevogadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimaRenovacaoId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefreshAnteriorId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefreshSucessorProtegido = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessoes_nativas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sessoes_nativas_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sessoes_nativas_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_SessaoNativaId",
                table: "refresh_tokens",
                column: "SessaoNativaId");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_devices_sessao_nativa_id",
                table: "mobile_devices",
                column: "sessao_nativa_id");

            migrationBuilder.CreateIndex(
                name: "IX_sessoes_nativas_EmpresaId",
                table: "sessoes_nativas",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_sessoes_nativas_UsuarioId_DispositivoId",
                table: "sessoes_nativas",
                columns: new[] { "UsuarioId", "DispositivoId" });

            migrationBuilder.AddForeignKey(
                name: "FK_mobile_devices_sessoes_nativas_sessao_nativa_id",
                table: "mobile_devices",
                column: "sessao_nativa_id",
                principalTable: "sessoes_nativas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_refresh_tokens_sessoes_nativas_SessaoNativaId",
                table: "refresh_tokens",
                column: "SessaoNativaId",
                principalTable: "sessoes_nativas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_mobile_devices_sessoes_nativas_sessao_nativa_id",
                table: "mobile_devices");

            migrationBuilder.DropForeignKey(
                name: "FK_refresh_tokens_sessoes_nativas_SessaoNativaId",
                table: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "sessoes_nativas");

            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_SessaoNativaId",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "IX_mobile_devices_sessao_nativa_id",
                table: "mobile_devices");

            migrationBuilder.DropColumn(
                name: "SessaoNativaId",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "sessao_nativa_id",
                table: "mobile_devices");
        }
    }
}
