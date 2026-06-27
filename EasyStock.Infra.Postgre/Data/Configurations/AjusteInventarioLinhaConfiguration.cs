using EasyStock.Domain.ValueObjects;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class AjusteInventarioLinhaConfiguration : IEntityTypeConfiguration<AjusteInventarioLinha>
    {
        public void Configure(EntityTypeBuilder<AjusteInventarioLinha> builder)
        {
            builder.ToTable("ajustes_inventario_linha");
            builder.HasKey(l => l.Id);

            builder.Property(l => l.QtdAntes)
                .HasConversion(
                    q => q == null ? 0m : q.Value,
                    value => value >= 0 ? Quantidade.From(value) : Quantidade.Zero)
                .HasColumnType("numeric(18,3)");
            builder.Property(l => l.QtdDepois)
                .HasConversion(
                    q => q == null ? 0m : q.Value,
                    value => value >= 0 ? Quantidade.From(value) : Quantidade.Zero)
                .HasColumnType("numeric(18,3)");
            builder.Property(l => l.Delta).HasColumnType("numeric(18,3)");
            builder.Property(l => l.CustoUnitarioSnapshot)
                .HasConversion(
                    d => d == null ? 0m : d.Valor,
                    value => value >= 0 ? Dinheiro.FromDecimal(value) : Dinheiro.Zero)
                .HasColumnType("decimal(18,2)");
            builder.Property(l => l.Tipo).HasConversion<string>().IsRequired().HasMaxLength(20);

            builder.HasOne<Empresa>().WithMany()
                .HasForeignKey(l => l.EmpresaId).OnDelete(DeleteBehavior.Restrict);
            // AjusteInventario -> Linhas (Cascade) configurado em AjusteInventarioConfiguration.
            builder.HasOne<Produto>().WithMany()
                .HasForeignKey(l => l.ProdutoId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<ItemEstoque>().WithMany()
                .HasForeignKey(l => l.ItemEstoqueId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
