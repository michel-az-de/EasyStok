using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferenciaMenuUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "preferencias_menu_usuario",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    favoritos_menu = table.Column<string>(type: "jsonb", nullable: false),
                    AtualizadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preferencias_menu_usuario", x => x.Id);
                    table.ForeignKey(
                        name: "FK_preferencias_menu_usuario_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_preferencias_menu_usuario_lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_preferencias_menu_usuario_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_preferencias_menu_usuario_EmpresaId_LojaId",
                table: "preferencias_menu_usuario",
                columns: new[] { "EmpresaId", "LojaId" });

            migrationBuilder.CreateIndex(
                name: "IX_preferencias_menu_usuario_LojaId",
                table: "preferencias_menu_usuario",
                column: "LojaId");

            migrationBuilder.CreateIndex(
                name: "IX_preferencias_menu_usuario_UsuarioId_LojaId",
                table: "preferencias_menu_usuario",
                columns: new[] { "UsuarioId", "LojaId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "preferencias_menu_usuario");
        }
    }
}
