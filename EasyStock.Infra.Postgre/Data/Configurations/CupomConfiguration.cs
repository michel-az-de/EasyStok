using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class CupomConfiguration : IEntityTypeConfiguration<Cupom>
{
    public void Configure(EntityTypeBuilder<Cupom> b)
    {
        b.ToTable("Cupons");
        b.HasKey(x => x.Id);
        b.Property(x => x.Codigo).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.Codigo).IsUnique();
        b.Property(x => x.Valor).HasColumnType("decimal(10,2)");
        b.Property(x => x.TipoDesconto).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.Ativo);
    }
}
