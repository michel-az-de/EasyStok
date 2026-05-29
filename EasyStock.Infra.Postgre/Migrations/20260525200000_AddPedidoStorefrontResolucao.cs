using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// TASK-EZ-APROVAR-001: campos de resolução Storefront pela Babá no agregado Pedido.
    /// Migration aditiva — todos os campos são nullable, zero impacto em rows existentes.
    ///
    /// <list type="bullet">
    ///   <item><c>aprovado_em</c> / <c>aprovado_por_usuario_id</c> — preenchidos na aprovação.</item>
    ///   <item><c>recusado_em</c> / <c>recusado_por_usuario_id</c> — preenchidos na recusa.</item>
    ///   <item><c>motivo_recusa</c> (varchar 40) / <c>mensagem_recusa_cliente</c> (varchar 280).</item>
    ///   <item><c>Status</c> ampliado de varchar(20) → varchar(32) para acomodar
    ///   <c>"aguardando_aprovacao_baba"</c> (25 chars).</item>
    /// </list>
    /// </summary>
    public partial class AddPedidoStorefrontResolucao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "pedidos",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<DateTime>(
                name: "aprovado_em",
                table: "pedidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "aprovado_por_usuario_id",
                table: "pedidos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "recusado_em",
                table: "pedidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recusado_por_usuario_id",
                table: "pedidos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_recusa",
                table: "pedidos",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mensagem_recusa_cliente",
                table: "pedidos",
                type: "character varying(280)",
                maxLength: 280,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "mensagem_recusa_cliente", table: "pedidos");
            migrationBuilder.DropColumn(name: "motivo_recusa", table: "pedidos");
            migrationBuilder.DropColumn(name: "recusado_por_usuario_id", table: "pedidos");
            migrationBuilder.DropColumn(name: "recusado_em", table: "pedidos");
            migrationBuilder.DropColumn(name: "aprovado_por_usuario_id", table: "pedidos");
            migrationBuilder.DropColumn(name: "aprovado_em", table: "pedidos");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "pedidos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);
        }
    }
}
