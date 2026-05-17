using EasyStock.Domain.Entities.Financeiro;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class ParcelaPagarConfiguration : IEntityTypeConfiguration<ParcelaPagar>
{
    public void Configure(EntityTypeBuilder<ParcelaPagar> b)
    {
        b.ToTable("parcelas_pagar");
        b.HasKey(x => x.Id);

        b.Property(x => x.Numero).IsRequired();
        b.Property(x => x.Valor).HasColumnType("numeric(14,2)");
        b.Property(x => x.ValorPago).HasColumnType("numeric(14,2)").HasDefaultValue(0m);
        b.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(30);
        b.Property(x => x.MetodoPlanejado).HasMaxLength(20);

        b.Property(x => x.Versao)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        b.HasMany(x => x.Pagamentos)
            .WithOne(p => p.ParcelaPagar)
            .HasForeignKey(p => p.ParcelaPagarId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.ContaPagarId, x.Numero }).IsUnique().HasDatabaseName("ux_parcelas_pagar_conta_numero");
        b.HasIndex(x => new { x.EmpresaId, x.DataVencimento, x.Status }).HasDatabaseName("ix_parcelas_pagar_empresa_vencimento_status");
        b.HasIndex(x => new { x.EmpresaId, x.Status }).HasDatabaseName("ix_parcelas_pagar_empresa_status");

        b.Ignore(x => x.Saldo);
    }
}
