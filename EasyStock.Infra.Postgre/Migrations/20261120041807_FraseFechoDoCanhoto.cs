using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class FraseFechoDoCanhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FraseFechoCanhoto",
                table: "configuracoes_atendimento",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FraseFechoCanhoto",
                table: "configuracoes_atendimento");
        }
    }
}
