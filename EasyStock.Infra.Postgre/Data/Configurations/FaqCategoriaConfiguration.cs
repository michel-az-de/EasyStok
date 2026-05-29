namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public sealed class FaqCategoriaConfiguration : IEntityTypeConfiguration<FaqCategoria>
    {
        public void Configure(EntityTypeBuilder<FaqCategoria> builder)
        {
            builder.ToTable("faq_categorias");
            builder.HasKey(c => c.Id);

            builder.Property(c => c.Nome).IsRequired().HasMaxLength(80);
            builder.Property(c => c.Slug).IsRequired().HasMaxLength(80);
            builder.Property(c => c.Descricao).HasMaxLength(500);
            builder.Property(c => c.Icone).HasMaxLength(60);
            builder.Property(c => c.Publica).HasDefaultValue(true);

            builder.HasIndex(c => c.Slug).IsUnique().HasDatabaseName("ux_faq_categorias_slug");
            builder.HasIndex(c => new { c.Publica, c.Ordem }).HasDatabaseName("ix_faq_categorias_publica_ordem");

            builder.HasMany(c => c.Itens)
                .WithOne(i => i.Categoria)
                .HasForeignKey(i => i.CategoriaId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
