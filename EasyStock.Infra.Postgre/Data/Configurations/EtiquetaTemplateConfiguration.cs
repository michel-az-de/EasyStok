using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class EtiquetaTemplateSistemaConfiguration : IEntityTypeConfiguration<EtiquetaTemplateSistema>
{
    public void Configure(EntityTypeBuilder<EtiquetaTemplateSistema> b)
    {
        b.ToTable("etiqueta_templates_sistema");
        b.HasKey(x => x.Id);
        b.Property(x => x.Codigo).IsRequired().HasMaxLength(60);
        b.Property(x => x.Nome).IsRequired().HasMaxLength(120);
        b.Property(x => x.Descricao).HasColumnType("text");
        b.Property(x => x.LayoutJson).IsRequired().HasColumnType("text");
        b.HasIndex(x => x.Codigo).IsUnique();
    }
}

public class EtiquetaTemplateConfiguration : IEntityTypeConfiguration<EtiquetaTemplate>
{
    public void Configure(EntityTypeBuilder<EtiquetaTemplate> b)
    {
        b.ToTable("etiqueta_templates");
        b.HasKey(x => x.Id);
        b.Property(x => x.Nome).IsRequired().HasMaxLength(120);
        b.Property(x => x.LayoutJson).IsRequired().HasColumnType("text");

        // Optimistic concurrency via xmin (system column do Postgres — não exige migration)
        b.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
        b.Ignore(x => x.RowVersion);

        b.HasIndex(x => new { x.EmpresaId, x.Nome }).IsUnique();

        b.HasOne(x => x.Empresa)
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.BaseSistema)
            .WithMany()
            .HasForeignKey(x => x.BaseSistemaId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class EtiquetaEmpresaDefaultConfiguration : IEntityTypeConfiguration<EtiquetaEmpresaDefault>
{
    public void Configure(EntityTypeBuilder<EtiquetaEmpresaDefault> b)
    {
        b.ToTable("etiqueta_empresa_default");
        b.HasKey(x => x.EmpresaId);
        b.Property(x => x.TemplateOrigem).IsRequired().HasMaxLength(10);

        b.HasOne(x => x.Empresa)
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
