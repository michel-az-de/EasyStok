using EasyStock.Domain.Entities.Fiscal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Fiscal;

public sealed class NotaFiscalCertificadoA1Configuration : IEntityTypeConfiguration<NotaFiscalCertificadoA1>
{
    public void Configure(EntityTypeBuilder<NotaFiscalCertificadoA1> b)
    {
        b.ToTable("nota_fiscal_certificado_a1");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.EmpresaId).HasColumnName("empresa_id").IsRequired();
        b.Property(x => x.PfxCifrado).HasColumnName("pfx_cifrado").HasColumnType("bytea").IsRequired();
        b.Property(x => x.SenhaCifrada).HasColumnName("senha_cifrada").HasColumnType("bytea").IsRequired();
        b.Property(x => x.Iv).HasColumnName("iv").HasColumnType("bytea").IsRequired();
        b.Property(x => x.Tag).HasColumnName("tag").HasColumnType("bytea").IsRequired();
        b.Property(x => x.KekId).HasColumnName("kek_id").HasMaxLength(40).IsRequired();
        b.Property(x => x.NomeTitular).HasColumnName("nome_titular").HasMaxLength(120).IsRequired();
        b.Property(x => x.DocumentoTitular).HasColumnName("documento_titular").HasMaxLength(14).IsRequired();
        b.Property(x => x.ValidoDe).HasColumnName("valido_de").IsRequired();
        b.Property(x => x.ValidoAte).HasColumnName("valido_ate").IsRequired();
        b.Property(x => x.Ativo).HasColumnName("ativo").HasDefaultValue(true).IsRequired();
        b.Property(x => x.CriadoPorUsuarioId).HasColumnName("criado_por_usuario_id").IsRequired();
        b.Property(x => x.CriadoEm).HasColumnName("created_at").IsRequired();
        b.Property(x => x.AlteradoEm).HasColumnName("updated_at").IsRequired();

        b.HasIndex(x => new { x.EmpresaId, x.Ativo })
            .HasFilter("ativo = true")
            .HasDatabaseName("ix_nota_fiscal_cert_empresa_ativo");
    }
}
