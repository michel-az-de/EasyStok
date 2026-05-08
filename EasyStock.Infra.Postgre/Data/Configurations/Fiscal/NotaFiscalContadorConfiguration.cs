using EasyStock.Domain.Entities.Fiscal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Fiscal;

public sealed class NotaFiscalContadorConfiguration : IEntityTypeConfiguration<NotaFiscalContador>
{
    public void Configure(EntityTypeBuilder<NotaFiscalContador> b)
    {
        b.ToTable("nota_fiscal_contador");
        b.HasKey(x => new { x.EmpresaId, x.LojaId, x.Modelo, x.Serie });

        b.Property(x => x.EmpresaId).HasColumnName("empresa_id");
        b.Property(x => x.LojaId).HasColumnName("loja_id");
        b.Property(x => x.Modelo).HasColumnName("modelo").HasConversion<short>();
        b.Property(x => x.Serie).HasColumnName("serie");
        b.Property(x => x.UltimoNumero).HasColumnName("ultimo_numero").HasDefaultValue(0).IsRequired();
        b.Property(x => x.AtualizadoEm).HasColumnName("atualizado_em").IsRequired();
    }
}
