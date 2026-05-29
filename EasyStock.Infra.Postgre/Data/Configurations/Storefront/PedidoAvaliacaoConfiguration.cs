using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

public class PedidoAvaliacaoConfiguration : IEntityTypeConfiguration<PedidoAvaliacao>
{
    public void Configure(EntityTypeBuilder<PedidoAvaliacao> builder)
    {
        builder.ToTable("pedido_avaliacao");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.PedidoId).IsRequired();
        builder.Property(a => a.ClienteId).IsRequired();
        builder.Property(a => a.EmpresaId).IsRequired();
        builder.Property(a => a.Estrelas).IsRequired();
        builder.Property(a => a.RecomendariaParaAmigos).IsRequired();

        builder.Property(a => a.Comentario).HasMaxLength(500);
        builder.Property(a => a.FotoUrl).HasMaxLength(500);
        builder.Property(a => a.RespostaDaBaba).HasMaxLength(500);

        builder.Property(a => a.SolicitadoEm).IsRequired();
        builder.Property(a => a.RespondidoEm);
        builder.Property(a => a.OcultadoEm);
        builder.Property(a => a.RespondidaEmPorBaba);

        // Invariante: 1 avaliação por pedido (acceptance criteria explicito).
        builder.HasIndex(a => a.PedidoId)
            .IsUnique()
            .HasDatabaseName("uq_pedido_avaliacao_pedido");

        // Lookup por loja para listagem pública (Babá enxerga avaliações de seus pedidos).
        builder.HasIndex(a => new { a.EmpresaId, a.RespondidoEm })
            .HasDatabaseName("ix_pedido_avaliacao_empresa_respondido");

        // FK Pedido — RESTRICT: não permite deletar pedido com avaliação (auditoria histórica).
        builder.HasOne<Pedido>()
            .WithMany()
            .HasForeignKey(a => a.PedidoId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK Cliente — RESTRICT: preserva avaliação mesmo se cliente for marcado como removido.
        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(a => a.ClienteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
