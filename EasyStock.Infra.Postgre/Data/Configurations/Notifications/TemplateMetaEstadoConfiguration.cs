using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Infra.Postgre.Data.Configurations.Notifications;

/// <summary>Estado dos templates da Meta (N6). Global: sem <c>EmpresaId</c>, fora do filtro de tenant e da RLS.</summary>
public class TemplateMetaEstadoConfiguration : IEntityTypeConfiguration<TemplateMetaEstado>
{
    public void Configure(EntityTypeBuilder<TemplateMetaEstado> b)
    {
        b.ToTable("notif_templates_meta_estado");
        b.HasKey(x => new { x.Nome, x.Idioma });

        b.Property(x => x.Nome).HasMaxLength(512).IsRequired();
        b.Property(x => x.Idioma).HasMaxLength(16).IsRequired();
        b.Property(x => x.CategoriaAtual).HasMaxLength(20).IsRequired();
        b.Property(x => x.AtualizadoEm).IsRequired();
    }
}
