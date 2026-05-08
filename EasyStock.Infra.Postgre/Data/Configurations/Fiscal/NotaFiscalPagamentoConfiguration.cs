using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Fiscal;

public sealed class NotaFiscalPagamentoConfiguration : IEntityTypeConfiguration<NotaFiscalPagamento>
{
    public void Configure(EntityTypeBuilder<NotaFiscalPagamento> b)
    {
        b.ToTable("nota_fiscal_pagamento");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.NotaFiscalId).HasColumnName("nota_fiscal_id").IsRequired();
        b.Property(x => x.EmpresaId).HasColumnName("empresa_id").IsRequired();
        b.Property(x => x.Ordem).HasColumnName("ordem").IsRequired();

        b.Property(x => x.FormaPagamento)
            .HasColumnName("forma_pagamento_codigo")
            .HasConversion(
                v => ((byte)v).ToString("D2"),
                s => (Domain.Enums.Fiscal.FormaPagamentoFiscal)byte.Parse(s))
            .HasMaxLength(2)
            .IsRequired();

        b.Property(x => x.Valor)
            .HasColumnName("valor")
            .HasColumnType("numeric(14,2)")
            .HasConversion(d => d.Valor, dec => Dinheiro.FromDecimal(dec))
            .IsRequired();

        b.Property(x => x.BandeiraCartao).HasColumnName("bandeira_cartao").HasMaxLength(20);
        b.Property(x => x.CnpjCredenciadora).HasColumnName("cnpj_credenciadora").HasMaxLength(14);
        b.Property(x => x.Nsu).HasColumnName("nsu").HasMaxLength(20);

        b.Property(x => x.Troco)
            .HasColumnName("troco")
            .HasColumnType("numeric(14,2)")
            .HasConversion(d => d.Valor, dec => Dinheiro.FromDecimal(dec))
            .HasDefaultValue(Dinheiro.Zero);

        b.HasIndex(x => x.NotaFiscalId).HasDatabaseName("ix_nota_fiscal_pagamento_nf");
    }
}
