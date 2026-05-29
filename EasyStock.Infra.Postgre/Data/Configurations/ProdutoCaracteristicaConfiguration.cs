namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class ProdutoCaracteristicaConfiguration : IEntityTypeConfiguration<ProdutoCaracteristica>
    {
        public void Configure(EntityTypeBuilder<ProdutoCaracteristica> builder)
        {
            builder.ToTable("produto_caracteristicas");
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Nome).IsRequired().HasMaxLength(120);
            builder.Property(c => c.VariacaoPadrao).HasMaxLength(120);
            builder.Property(c => c.Descricao).HasColumnType("text");

            builder.HasOne(c => c.Empresa).WithMany(e => e.CaracteristicasProduto).HasForeignKey(c => c.EmpresaId);
            builder.HasOne(c => c.Produto).WithMany(p => p.Caracteristicas).HasForeignKey(c => c.ProdutoId);
            builder.HasOne(c => c.Variacao).WithMany().HasForeignKey(c => c.VariacaoId).IsRequired(false);
        }
    }
}
