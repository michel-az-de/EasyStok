using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class VendaConfiguration : IEntityTypeConfiguration<Venda>
    {
        public void Configure(EntityTypeBuilder<Venda> builder)
        {
            builder.ToTable("vendas");
            builder.HasKey(v => v.Id);
            builder.Property(v => v.Canal).HasConversion<string>().IsRequired().HasMaxLength(50);
            builder.Property(v => v.Natureza).HasConversion<string>().IsRequired().HasMaxLength(50);
            builder.Property(v => v.NumeroNotaFiscal).HasMaxLength(80);
            builder.Property(v => v.ValorTotal)
                .HasConversion(d => d.Valor, value => Dinheiro.FromDecimal(value))
                .HasColumnType("decimal(18,2)");

            builder.HasOne(v => v.Empresa).WithMany(e => e.Vendas).HasForeignKey(v => v.EmpresaId);
        }
    }
}
