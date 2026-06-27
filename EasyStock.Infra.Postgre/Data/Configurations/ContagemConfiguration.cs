namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class ContagemConfiguration : IEntityTypeConfiguration<Contagem>
    {
        public void Configure(EntityTypeBuilder<Contagem> builder)
        {
            builder.ToTable("contagens");
            builder.HasKey(c => c.Id);

            builder.Property(c => c.Escopo).HasConversion<string>().IsRequired().HasMaxLength(20);
            builder.Property(c => c.Modo).HasConversion<string>().IsRequired().HasMaxLength(20);
            builder.Property(c => c.EstrategiaLote).HasConversion<string>().IsRequired().HasMaxLength(20);
            builder.Property(c => c.Status).HasConversion<string>().IsRequired().HasMaxLength(20);
            builder.Property(c => c.Observacao).HasMaxLength(1000);

            // Derivada — nunca persistida (espelha ProdutoConfiguration.CompletudePercent).
            builder.Ignore(c => c.EstaTerminal);

            builder.HasOne(c => c.Empresa).WithMany()
                .HasForeignKey(c => c.EmpresaId).OnDelete(DeleteBehavior.Restrict);
            builder.HasMany(c => c.Itens).WithOne(i => i.Contagem)
                .HasForeignKey(i => i.ContagemId).OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(c => new { c.EmpresaId, c.Status })
                .HasDatabaseName("ix_contagens_empresa_status");
        }
    }
}
