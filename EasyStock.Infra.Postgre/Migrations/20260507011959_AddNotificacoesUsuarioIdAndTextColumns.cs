using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificacoesUsuarioIdAndTextColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSeedData",
                table: "empresas");

            migrationBuilder.AddColumn<Guid>(
                name: "UsuarioId",
                table: "notificacoes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ErroUltimaTentativa",
                table: "notif_outbox_mensagens",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ErroDetalhado",
                table: "notif_logs_envio",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "system_error_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Details = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AdminEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_error_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_EmpresaId_UsuarioId_Lida_CriadaEm",
                table: "notificacoes",
                columns: new[] { "EmpresaId", "UsuarioId", "Lida", "CriadaEm" });

            migrationBuilder.CreateIndex(
                name: "IX_system_error_logs_CriadoEm",
                table: "system_error_logs",
                column: "CriadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_system_error_logs_Source_Level",
                table: "system_error_logs",
                columns: new[] { "Source", "Level" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_error_logs");

            migrationBuilder.DropIndex(
                name: "IX_notificacoes_EmpresaId_UsuarioId_Lida_CriadaEm",
                table: "notificacoes");

            migrationBuilder.DropColumn(
                name: "UsuarioId",
                table: "notificacoes");

            migrationBuilder.AlterColumn<string>(
                name: "ErroUltimaTentativa",
                table: "notif_outbox_mensagens",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ErroDetalhado",
                table: "notif_logs_envio",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSeedData",
                table: "empresas",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
