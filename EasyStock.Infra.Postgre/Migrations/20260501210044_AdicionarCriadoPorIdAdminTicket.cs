using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarCriadoPorIdAdminTicket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirmado",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CriadoPorId",
                table: "admin_tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "email_confirmation_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Confirmado = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ConfirmadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IpCriacao = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_confirmation_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_confirmation_tokens_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_tickets_CriadoPorId",
                table: "admin_tickets",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_email_confirmation_tokens_ExpiraEm",
                table: "email_confirmation_tokens",
                column: "ExpiraEm");

            migrationBuilder.CreateIndex(
                name: "IX_email_confirmation_tokens_Token",
                table: "email_confirmation_tokens",
                column: "Token");

            migrationBuilder.CreateIndex(
                name: "IX_email_confirmation_tokens_UsuarioId",
                table: "email_confirmation_tokens",
                column: "UsuarioId");

            migrationBuilder.AddForeignKey(
                name: "FK_admin_tickets_usuarios_CriadoPorId",
                table: "admin_tickets",
                column: "CriadoPorId",
                principalTable: "usuarios",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_admin_tickets_usuarios_CriadoPorId",
                table: "admin_tickets");

            migrationBuilder.DropTable(
                name: "email_confirmation_tokens");

            migrationBuilder.DropIndex(
                name: "IX_admin_tickets_CriadoPorId",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "EmailConfirmado",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "CriadoPorId",
                table: "admin_tickets");
        }
    }
}
