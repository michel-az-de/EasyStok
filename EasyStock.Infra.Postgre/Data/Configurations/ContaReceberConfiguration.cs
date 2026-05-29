using EasyStock.Domain.Entities.Financeiro;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class ContaReceberConfiguration : IEntityTypeConfiguration<ContaReceber>
{
    public void Configure(EntityTypeBuilder<ContaReceber> b)
    {
        b.ToTable("contas_receber");
        b.HasKey(x => x.Id);

        b.Property(x => x.Descricao).IsRequired().HasMaxLength(200);
        b.Property(x => x.Observacoes).HasMaxLength(4000);
        b.Property(x => x.MotivoCancelamento).HasMaxLength(500);
        b.Property(x => x.DocumentoReferencia).HasMaxLength(120);

        b.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(30);
        b.Property(x => x.Origem).HasConversion<string>().IsRequired().HasMaxLength(30);

        b.Property(x => x.ValorTotal).HasColumnType("numeric(14,2)").HasDefaultValue(0m);

        b.Property(x => x.Versao)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Loja).WithMany().HasForeignKey(x => x.LojaId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Categoria).WithMany().HasForeignKey(x => x.CategoriaFinanceiraId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CentroCusto).WithMany().HasForeignKey(x => x.CentroCustoId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Fatura).WithMany().HasForeignKey(x => x.FaturaId).OnDelete(DeleteBehavior.SetNull);

        b.HasMany(x => x.Parcelas)
            .WithOne(p => p.ContaReceber)
            .HasForeignKey(p => p.ContaReceberId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Alteracoes)
            .WithOne(a => a.ContaReceber)
            .HasForeignKey(a => a.ContaReceberId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.EmpresaId, x.Status }).HasDatabaseName("ix_contas_receber_empresa_status");
        b.HasIndex(x => new { x.EmpresaId, x.ClienteId }).HasDatabaseName("ix_contas_receber_empresa_cliente");
        b.HasIndex(x => new { x.EmpresaId, x.CategoriaFinanceiraId }).HasDatabaseName("ix_contas_receber_empresa_categoria");
        b.HasIndex(x => new { x.EmpresaId, x.CentroCustoId }).HasDatabaseName("ix_contas_receber_empresa_centro");
        b.HasIndex(x => new { x.EmpresaId, x.DataEmissao }).HasDatabaseName("ix_contas_receber_empresa_emissao");

        b.HasIndex(x => new { x.EmpresaId, x.Origem, x.OrigemRefId })
            .IsUnique()
            .HasDatabaseName("ux_contas_receber_empresa_origem_ref")
            .HasFilter("\"OrigemRefId\" IS NOT NULL");

        b.HasIndex(x => new { x.EmpresaId, x.DocumentoReferencia })
            .IsUnique()
            .HasDatabaseName("ux_contas_receber_empresa_documento_ref")
            .HasFilter("\"DocumentoReferencia\" IS NOT NULL");

        b.Ignore(x => x.TotalRecebido);
        b.Ignore(x => x.Pendente);
    }
}

public class ContaReceberAlteracaoConfiguration : IEntityTypeConfiguration<ContaReceberAlteracao>
{
    public void Configure(EntityTypeBuilder<ContaReceberAlteracao> b)
    {
        b.ToTable("contas_receber_alteracoes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Campo).IsRequired().HasMaxLength(60);
        b.Property(x => x.ValorAntigo).HasColumnType("text");
        b.Property(x => x.ValorNovo).HasColumnType("text");
        b.Property(x => x.AlteradoPorNome).HasMaxLength(120);
        b.Property(x => x.Origem).HasMaxLength(20);

        b.HasIndex(x => new { x.ContaReceberId, x.AlteradoEm }).HasDatabaseName("ix_contas_receber_alteracoes_conta_data");
    }
}
