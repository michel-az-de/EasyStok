using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class LeadPublicoConfiguration : IEntityTypeConfiguration<LeadPublico>
{
    public void Configure(EntityTypeBuilder<LeadPublico> b)
    {
        b.ToTable("leads_publicos");
        b.HasKey(x => x.Id);

        b.Property(x => x.Nome).IsRequired().HasMaxLength(150);

        // VO EmailAddress: HasConversion preserva pattern do projeto (ver do-not-do.md).
        b.Property(x => x.Email)
            .IsRequired()
            .HasMaxLength(255)
            .HasConversion(v => v.Value, v => EmailAddress.From(v));

        b.Property(x => x.Telefone)
            .HasMaxLength(32)
            .HasConversion(
                v => v == null ? null : v.Value,
                v => v == null ? null : Telefone.From(v));

        b.Property(x => x.Empresa).HasMaxLength(150);
        b.Property(x => x.Mensagem).HasColumnType("text");
        b.Property(x => x.Origem).HasConversion<int>();
        b.Property(x => x.TipoNegocio).HasMaxLength(80);
        b.Property(x => x.IpOrigem).HasMaxLength(64);
        b.Property(x => x.UserAgent).HasMaxLength(512);
        b.Property(x => x.UtmSource).HasMaxLength(120);
        b.Property(x => x.UtmMedium).HasMaxLength(120);
        b.Property(x => x.UtmCampaign).HasMaxLength(120);

        b.HasIndex(x => x.CriadoEm);
        b.HasIndex(x => x.IpOrigem);
        b.HasIndex(x => x.Origem);
        b.HasIndex(x => x.ProcessadoEm);
    }
}
