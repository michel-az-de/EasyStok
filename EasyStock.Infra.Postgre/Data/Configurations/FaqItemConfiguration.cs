using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public sealed class FaqItemConfiguration : IEntityTypeConfiguration<FaqItem>
    {
        public void Configure(EntityTypeBuilder<FaqItem> builder)
        {
            builder.ToTable("faq_itens");
            builder.HasKey(i => i.Id);

            builder.Property(i => i.Titulo).IsRequired().HasMaxLength(200);
            builder.Property(i => i.Slug).IsRequired().HasMaxLength(200);
            builder.Property(i => i.Conteudo).IsRequired().HasMaxLength(20_000);
            builder.Property(i => i.ConteudoBusca).IsRequired().HasMaxLength(20_000);
            builder.Property(i => i.TagsCsv).HasMaxLength(500).HasDefaultValue(string.Empty);
            builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            builder.Property(i => i.Visualizacoes).HasDefaultValue(0);
            builder.Property(i => i.UtilCount).HasDefaultValue(0);
            builder.Property(i => i.NaoUtilCount).HasDefaultValue(0);

            builder.Ignore(i => i.Tags);

            builder.HasIndex(i => new { i.CategoriaId, i.Slug })
                .IsUnique()
                .HasDatabaseName("ux_faq_itens_categoria_slug");

            builder.HasIndex(i => new { i.Status, i.PublicadoEm })
                .HasDatabaseName("ix_faq_itens_status_publicado");

            builder.HasIndex(i => i.Visualizacoes)
                .HasDatabaseName("ix_faq_itens_visualizacoes");
        }
    }
}
