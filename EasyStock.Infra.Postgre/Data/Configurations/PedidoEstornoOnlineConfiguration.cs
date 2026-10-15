namespace EasyStock.Infra.Postgre.Data.Configurations;

public sealed class PedidoEstornoOnlineConfiguration : IEntityTypeConfiguration<PedidoEstornoOnline>
{
    public void Configure(EntityTypeBuilder<PedidoEstornoOnline> b)
    {
        b.ToTable("pedido_estornos_online", t =>
        {
            t.HasCheckConstraint("ck_estorno_online_valor", "\"Valor\" > 0");
            t.HasCheckConstraint("ck_estorno_online_confirmacao", "(\"Situacao\" = 'confirmado') = (\"MovimentoCaixaId\" IS NOT NULL AND \"ConfirmadoEm\" IS NOT NULL)");
        });
        b.HasKey(e => e.Id);
        b.Property(e => e.Valor).HasColumnType("numeric(14,2)");
        b.Property(e => e.PagamentoExternoId).HasMaxLength(30).IsRequired();
        b.Property(e => e.Motivo).HasMaxLength(500).IsRequired();
        b.Property(e => e.UsuarioNome).HasMaxLength(120);
        b.Property(e => e.Situacao).HasMaxLength(20).IsRequired();
        b.Property(e => e.EstornoExternoId).HasMaxLength(60);
        b.Property(e => e.Detalhe).HasMaxLength(300);
        b.HasIndex(e => new { e.EmpresaId, e.PedidoId });
        b.HasIndex(e => new { e.EmpresaId, e.PagamentoExternoId, e.EstornoExternoId }).IsUnique();
        b.HasOne<Pedido>().WithMany().HasForeignKey(e => e.PedidoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PedidoPagamento>().WithMany().HasForeignKey(e => e.PagamentoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MovimentoCaixa>().WithOne().HasForeignKey<PedidoEstornoOnline>(e => e.MovimentoCaixaId).OnDelete(DeleteBehavior.Restrict);
    }
}
