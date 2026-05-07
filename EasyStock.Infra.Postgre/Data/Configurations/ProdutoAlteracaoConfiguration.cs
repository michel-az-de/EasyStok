using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class ProdutoAlteracaoConfiguration : IEntityTypeConfiguration<ProdutoAlteracao>
{
    public void Configure(EntityTypeBuilder<ProdutoAlteracao> builder)
    {
        builder.ToTable("produto_alteracoes");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedOnAdd()
            .HasColumnType("uuid");

        builder.Property(a => a.EmpresaId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(a => a.ProdutoId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(a => a.UsuarioId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(a => a.Acao)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnType("character varying(50)");

        builder.Property(a => a.AlteracoesJson)
            .HasColumnType("text");

        builder.Property(a => a.Motivo)
            .HasMaxLength(100)
            .HasColumnType("character varying(100)");

        builder.Property(a => a.Observacao)
            .HasMaxLength(500)
            .HasColumnType("character varying(500)");

        builder.Property(a => a.AlteradoEm)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(a => a.Produto)
            .WithMany()
            .HasForeignKey(a => a.ProdutoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Usuario)
            .WithMany()
            .HasForeignKey(a => a.UsuarioId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(a => new { a.ProdutoId, a.AlteradoEm });
        builder.HasIndex(a => new { a.EmpresaId, a.ProdutoId });
        // Feed de "últimas alterações" por tenant — dashboard admin.
        builder.HasIndex(a => new { a.EmpresaId, a.AlteradoEm })
            .HasDatabaseName("ix_produto_alteracoes_empresa_alterado_em");
    }
}
