using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class FaturaContadorConfiguration : IEntityTypeConfiguration<FaturaContador>
{
    public void Configure(EntityTypeBuilder<FaturaContador> builder)
    {
        builder.ToTable("fatura_contador");
        builder.HasKey(c => new { c.EmpresaId, c.Ano });
        builder.Property(c => c.UltimoNumero).IsRequired().HasDefaultValue(0L);
    }
}
