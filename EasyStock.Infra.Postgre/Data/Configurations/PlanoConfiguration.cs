namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class PlanoConfiguration : IEntityTypeConfiguration<Plano>
    {
        public void Configure(EntityTypeBuilder<Plano> builder)
        {
            builder.ToTable("planos");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Nome).IsRequired().HasMaxLength(80);
            builder.Property(p => p.Descricao).HasMaxLength(500);
            builder.Property(p => p.PrecoMensal).HasColumnType("decimal(18,2)");
        }
    }
}
