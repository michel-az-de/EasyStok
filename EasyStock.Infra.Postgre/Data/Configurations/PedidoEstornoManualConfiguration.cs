namespace EasyStock.Infra.Postgre.Data.Configurations;

public sealed class PedidoEstornoManualConfiguration : IEntityTypeConfiguration<PedidoEstornoManual>
{
    public void Configure(EntityTypeBuilder<PedidoEstornoManual> b)
    {
        b.ToTable("pedido_estornos_manuais", t => t.HasCheckConstraint("ck_estorno_manual_valor", "\"Valor\" > 0"));
        b.HasKey(e => e.Id);
        b.Property(e => e.Valor).HasColumnType("numeric(14,2)");
        b.Property(e => e.Metodo).HasMaxLength(20).IsRequired();
        b.Property(e => e.Motivo).HasMaxLength(500).IsRequired();
        b.Property(e => e.Referencia).HasMaxLength(120).IsRequired();
        b.Property(e => e.UsuarioNome).HasMaxLength(120);
        b.HasIndex(e => new { e.EmpresaId, e.PedidoId });
        b.HasOne<Pedido>().WithMany().HasForeignKey(e => e.PedidoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PedidoPagamento>().WithMany().HasForeignKey(e => e.PagamentoId).OnDelete(DeleteBehavior.Restrict);
        // O mesmo ID identifica a operação idempotente e a saída indivisível no Caixa.
        b.HasOne<MovimentoCaixa>().WithOne().HasForeignKey<PedidoEstornoManual>(e => e.Id).OnDelete(DeleteBehavior.Restrict);
    }
}
