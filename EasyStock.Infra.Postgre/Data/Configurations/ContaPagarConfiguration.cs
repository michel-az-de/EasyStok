using EasyStock.Domain.Entities.Financeiro;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class ContaPagarConfiguration : IEntityTypeConfiguration<ContaPagar>
{
    public void Configure(EntityTypeBuilder<ContaPagar> b)
    {
        b.ToTable("contas_pagar");
        b.HasKey(x => x.Id);

        b.Property(x => x.Descricao).IsRequired().HasMaxLength(200);
        b.Property(x => x.Observacoes).HasMaxLength(4000);
        b.Property(x => x.MotivoCancelamento).HasMaxLength(500);
        b.Property(x => x.DocumentoReferencia).HasMaxLength(120);

        b.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(30);
        b.Property(x => x.Origem).HasConversion<string>().IsRequired().HasMaxLength(30);

        b.Property(x => x.ValorTotal).HasColumnType("numeric(14,2)").HasDefaultValue(0m);

        // xmin row version (PG system column) — sem migration, ValueGeneratedOnAddOrUpdate + ConcurrencyToken
        b.Property(x => x.Versao)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Loja).WithMany().HasForeignKey(x => x.LojaId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Fornecedor).WithMany().HasForeignKey(x => x.FornecedorId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Categoria).WithMany().HasForeignKey(x => x.CategoriaFinanceiraId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CentroCusto).WithMany().HasForeignKey(x => x.CentroCustoId).OnDelete(DeleteBehavior.SetNull);

        b.HasMany(x => x.Parcelas)
            .WithOne(p => p.ContaPagar)
            .HasForeignKey(p => p.ContaPagarId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Alteracoes)
            .WithOne(a => a.ContaPagar)
            .HasForeignKey(a => a.ContaPagarId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.EmpresaId, x.Status }).HasDatabaseName("ix_contas_pagar_empresa_status");
        b.HasIndex(x => new { x.EmpresaId, x.FornecedorId }).HasDatabaseName("ix_contas_pagar_empresa_fornecedor");
        b.HasIndex(x => new { x.EmpresaId, x.CategoriaFinanceiraId }).HasDatabaseName("ix_contas_pagar_empresa_categoria");
        b.HasIndex(x => new { x.EmpresaId, x.CentroCustoId }).HasDatabaseName("ix_contas_pagar_empresa_centro");
        b.HasIndex(x => new { x.EmpresaId, x.DataEmissao }).HasDatabaseName("ix_contas_pagar_empresa_emissao");

        // Idempotencia integracao (Origem + OrigemRefId UNIQUE filtrado)
        b.HasIndex(x => new { x.EmpresaId, x.Origem, x.OrigemRefId })
            .IsUnique()
            .HasDatabaseName("ux_contas_pagar_empresa_origem_ref")
            .HasFilter("\"OrigemRefId\" IS NOT NULL");

        // Idempotencia documento referencia
        b.HasIndex(x => new { x.EmpresaId, x.DocumentoReferencia })
            .IsUnique()
            .HasDatabaseName("ux_contas_pagar_empresa_documento_ref")
            .HasFilter("\"DocumentoReferencia\" IS NOT NULL");

        b.Ignore(x => x.TotalPago);
        b.Ignore(x => x.Pendente);
    }
}

public class ContaPagarAlteracaoConfiguration : IEntityTypeConfiguration<ContaPagarAlteracao>
{
    public void Configure(EntityTypeBuilder<ContaPagarAlteracao> b)
    {
        b.ToTable("contas_pagar_alteracoes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Campo).IsRequired().HasMaxLength(60);
        b.Property(x => x.ValorAntigo).HasColumnType("text");
        b.Property(x => x.ValorNovo).HasColumnType("text");
        b.Property(x => x.AlteradoPorNome).HasMaxLength(120);
        b.Property(x => x.Origem).HasMaxLength(20);

        b.HasIndex(x => new { x.ContaPagarId, x.AlteradoEm }).HasDatabaseName("ix_contas_pagar_alteracoes_conta_data");
    }
}
