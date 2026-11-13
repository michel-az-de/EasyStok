using EasyStock.Domain.Entities.Financeiro;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public sealed class MetodoPagamentoEmpresaConfiguration : IEntityTypeConfiguration<MetodoPagamentoEmpresa>
{
    public void Configure(EntityTypeBuilder<MetodoPagamentoEmpresa> b)
    {
        b.ToTable("metodos_pagamento_empresa");
        b.HasKey(c => c.Id);
        b.Property(c => c.Codigo).HasMaxLength(20).IsRequired();
        b.Property(c => c.Revisao).IsConcurrencyToken();
        b.HasOne<Empresa>().WithMany().HasForeignKey(c => c.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(c => new { c.EmpresaId, c.Codigo }).IsUnique();
    }
}
