namespace EasyStock.Infra.Postgre.Data.Configurations
{
    internal sealed class UsoIaConfiguration : IEntityTypeConfiguration<UsoIa>
    {
        public void Configure(EntityTypeBuilder<UsoIa> builder)
        {
            builder.ToTable("uso_ia");

            builder.HasKey(u => u.Id);

            builder.Property(u => u.EmpresaId).IsRequired();
            builder.Property(u => u.Ano).IsRequired();
            builder.Property(u => u.Mes).IsRequired();
            builder.Property(u => u.TotalGeracoes).IsRequired();
            builder.Property(u => u.TotalTokens).IsRequired();
            builder.Property(u => u.AtualizadoEm).IsRequired();

            builder.HasIndex(u => new { u.EmpresaId, u.Ano, u.Mes }).IsUnique();
        }
    }
}
