using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Infra.Postgre.Data.Configurations.Notifications;

public class VariavelTemplateCatalogoConfiguration : IEntityTypeConfiguration<VariavelTemplateCatalogo>
{
    public void Configure(EntityTypeBuilder<VariavelTemplateCatalogo> b)
    {
        b.ToTable("notif_variaveis_template_catalogo");
        b.HasKey(x => x.Id);

        b.Property(x => x.TipoEvento).HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.NomeVariavel).HasMaxLength(80).IsRequired();
        b.Property(x => x.Tipo).HasMaxLength(20).IsRequired();
        b.Property(x => x.Descricao).HasMaxLength(500);
        b.Property(x => x.Exemplo).HasMaxLength(500);

        b.HasIndex(x => new { x.TipoEvento, x.NomeVariavel }).IsUnique();
    }
}
