using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddBanners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "banners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TituloInterno = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Corpo = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ImagemStorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ImagemUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LinkAtivo = table.Column<bool>(type: "boolean", nullable: false),
                    LinkUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NovaAba = table.Column<bool>(type: "boolean", nullable: false),
                    TooltipAtivo = table.Column<bool>(type: "boolean", nullable: false),
                    TooltipTexto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    TamanhoModo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LarguraPx = table.Column<int>(type: "integer", nullable: true),
                    AlturaPx = table.Column<int>(type: "integer", nullable: true),
                    VisualizacaoUnica = table.Column<bool>(type: "boolean", nullable: false),
                    ExigeConfirmacao = table.Column<bool>(type: "boolean", nullable: false),
                    NotificarAoPublicar = table.Column<bool>(type: "boolean", nullable: false),
                    NotificadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    InicioEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FimEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Prioridade = table.Column<int>(type: "integer", nullable: false),
                    CriadoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_banners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "banner_confirmacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BannerId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RegistradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_banner_confirmacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_banner_confirmacoes_banners_BannerId",
                        column: x => x.BannerId,
                        principalTable: "banners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_banner_confirmacoes_usuario",
                table: "banner_confirmacoes",
                columns: new[] { "UsuarioId", "BannerId" });

            migrationBuilder.CreateIndex(
                name: "ux_banner_confirmacoes_banner_usuario_tipo",
                table: "banner_confirmacoes",
                columns: new[] { "BannerId", "UsuarioId", "Tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_banners_ativos",
                table: "banners",
                columns: new[] { "Ativo", "InicioEm", "FimEm" },
                filter: "\"Ativo\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "ix_banners_prioridade",
                table: "banners",
                column: "Prioridade");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "banner_confirmacoes");

            migrationBuilder.DropTable(
                name: "banners");
        }
    }
}
