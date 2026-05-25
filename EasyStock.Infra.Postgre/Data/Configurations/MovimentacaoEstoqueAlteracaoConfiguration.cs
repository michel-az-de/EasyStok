using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class MovimentacaoEstoqueAlteracaoConfiguration : IEntityTypeConfiguration<MovimentacaoEstoqueAlteracao>
{
    public void Configure(EntityTypeBuilder<MovimentacaoEstoqueAlteracao> builder)
    {
        builder.ToTable("movimentacao_estoque_alteracoes");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedOnAdd()
            .HasColumnType("uuid");

        builder.Property(a => a.EmpresaId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(a => a.MovimentacaoEstoqueId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(a => a.UsuarioId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(a => a.NomeUsuario)
            .HasMaxLength(256)
            .HasColumnType("character varying(256)");

        builder.Property(a => a.EmailUsuario)
            .HasMaxLength(256)
            .HasColumnType("character varying(256)");

        builder.Property(a => a.Acao)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnType("character varying(50)");

        builder.Property(a => a.Motivo)
            .HasMaxLength(100)
            .HasColumnType("character varying(100)");

        builder.Property(a => a.Observacao)
            .HasMaxLength(500)
            .HasColumnType("character varying(500)");

        builder.Property(a => a.AlteracoesJson)
            .HasColumnType("text");

        builder.Property(a => a.Ip)
            .HasMaxLength(64)
            .HasColumnType("character varying(64)");

        builder.Property(a => a.UserAgent)
            .HasMaxLength(500)
            .HasColumnType("character varying(500)");

        builder.Property(a => a.AlteradoEm)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(a => a.MovimentacaoEstoque)
            .WithMany()
            .HasForeignKey(a => a.MovimentacaoEstoqueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Usuario)
            .WithMany()
            .HasForeignKey(a => a.UsuarioId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(a => new { a.MovimentacaoEstoqueId, a.AlteradoEm });
        builder.HasIndex(a => new { a.EmpresaId, a.AlteradoEm });
        builder.HasIndex(a => a.UsuarioId);
    }
}
