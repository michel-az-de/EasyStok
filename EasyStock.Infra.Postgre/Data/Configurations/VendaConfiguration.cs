using EasyStock.Domain.ValueObjects;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class VendaConfiguration : IEntityTypeConfiguration<Venda>
    {
        public void Configure(EntityTypeBuilder<Venda> builder)
        {
            builder.ToTable("vendas");
            builder.HasKey(v => v.Id);
            builder.Property(v => v.Canal).HasConversion<string>().IsRequired().HasMaxLength(50);
            builder.Property(v => v.Natureza).HasConversion<string>().IsRequired().HasMaxLength(50);
            builder.Property(v => v.NumeroNotaFiscal).HasMaxLength(80);
            builder.Property(v => v.ValorTotal)
                .HasConversion(d => d.Valor, value => Dinheiro.FromDecimal(value))
                .HasColumnType("decimal(18,2)");

            builder.HasOne(v => v.Empresa).WithMany(e => e.Vendas).HasForeignKey(v => v.EmpresaId);

            builder.Property(x => x.LojaId).HasColumnType("uuid");
            builder.HasOne(x => x.Loja).WithMany(l => l.Vendas).HasForeignKey(x => x.LojaId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);

            // --- Colunas de relatório (PR-B) ---
            builder.Property(v => v.VendedorId).HasColumnType("uuid");
            builder.HasOne(v => v.Vendedor).WithMany().HasForeignKey(v => v.VendedorId)
                .IsRequired(false).OnDelete(DeleteBehavior.SetNull);

            builder.Property(v => v.FormaPagamentoPrincipal).HasMaxLength(20);

            builder.Property(v => v.Subtotal)
                .HasConversion(d => d == null ? (decimal?)null : d.Valor,
                               v => v == null ? null : Dinheiro.FromDecimal(v.Value))
                .HasColumnType("numeric(18,2)");

            builder.Property(v => v.ValorDesconto)
                .HasConversion(d => d == null ? (decimal?)null : d.Valor,
                               v => v == null ? null : Dinheiro.FromDecimal(v.Value))
                .HasColumnType("numeric(18,2)");
        }
    }
}
