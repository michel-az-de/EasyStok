using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class FaturaItemConfiguration : IEntityTypeConfiguration<FaturaItem>
{
    public void Configure(EntityTypeBuilder<FaturaItem> builder)
    {
        builder.ToTable("fatura_itens");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Descricao).IsRequired().HasMaxLength(300);
        builder.Property(i => i.Quantidade).HasColumnType("decimal(14,3)");
        builder.Property(i => i.PrecoUnitario).HasColumnType("decimal(14,2)");
        builder.Property(i => i.Subtotal).HasColumnType("decimal(14,2)");
        builder.Property(i => i.Tipo).HasConversion<string>().IsRequired().HasMaxLength(20);

        // FK explicita — antes EF gerava ClientSetNull por default; aqui a
        // semantica correta e Cascade (item nao existe sem fatura).
        builder.HasOne(i => i.Fatura)
            .WithMany(f => f.Itens)
            .HasForeignKey(i => i.FaturaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.FaturaId).HasDatabaseName("ix_fatura_itens_fatura_id");
        builder.HasIndex(i => new { i.FaturaId, i.Ordem }).HasDatabaseName("ix_fatura_itens_fatura_ordem");
    }
}
