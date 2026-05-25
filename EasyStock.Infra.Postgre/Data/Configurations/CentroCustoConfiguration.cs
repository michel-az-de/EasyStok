using EasyStock.Domain.Entities.Financeiro;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class CentroCustoConfiguration : IEntityTypeConfiguration<CentroCusto>
{
    public void Configure(EntityTypeBuilder<CentroCusto> b)
    {
        b.ToTable("centros_custo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Codigo).IsRequired().HasMaxLength(20);
        b.Property(x => x.Nome).IsRequired().HasMaxLength(80);
        b.Property(x => x.Descricao).HasMaxLength(400);
        b.Property(x => x.Ativo).HasDefaultValue(true);

        b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Loja).WithMany().HasForeignKey(x => x.LojaId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.EmpresaId, x.Ativo }).HasDatabaseName("ix_centros_custo_empresa_ativo");
        b.HasIndex(x => new { x.EmpresaId, x.LojaId }).HasDatabaseName("ix_centros_custo_empresa_loja");
        b.HasIndex(x => new { x.EmpresaId, x.Codigo })
            .IsUnique()
            .HasDatabaseName("ux_centros_custo_empresa_codigo");
    }
}
