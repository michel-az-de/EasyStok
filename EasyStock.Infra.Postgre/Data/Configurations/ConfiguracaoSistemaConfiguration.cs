using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class ConfiguracaoSistemaConfiguration : IEntityTypeConfiguration<ConfiguracaoSistema>
{
    public void Configure(EntityTypeBuilder<ConfiguracaoSistema> b)
    {
        b.ToTable("ConfiguracoesSistema");
        b.HasKey(x => x.Chave);
        b.Property(x => x.Chave).HasMaxLength(100).IsRequired();
        b.Property(x => x.Valor).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Descricao).HasMaxLength(500).IsRequired();
        b.Property(x => x.AlteradoPor).HasMaxLength(256).IsRequired();
    }
}
