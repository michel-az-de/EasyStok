using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class ProdutoComposicaoConfiguration : IEntityTypeConfiguration<ProdutoComposicao>
{
    public void Configure(EntityTypeBuilder<ProdutoComposicao> builder)
    {
        builder.ToTable("produtos_composicao");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).ValueGeneratedOnAdd().HasColumnType("uuid");
        builder.Property(c => c.EmpresaId).HasColumnType("uuid").IsRequired();
        builder.Property(c => c.ProdutoFinalId).HasColumnType("uuid").IsRequired();
        builder.Property(c => c.InsumoId).HasColumnType("uuid").IsRequired();
        builder.Property(c => c.LojaId).HasColumnType("uuid");

        builder.Property(c => c.Quantidade)
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(c => c.Unidade)
            .HasConversion<string>()
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(c => c.Observacao)
            .HasMaxLength(500)
            .HasColumnType("character varying(500)");

        builder.Property(c => c.OrdemExibicao).IsRequired();

        builder.Property(c => c.CriadoEm).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(c => c.AlteradoEm).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(c => c.CriadoPor).HasColumnType("uuid");
        builder.Property(c => c.AlteradoPor).HasColumnType("uuid");

        // Empresa: FK obrigatorio
        builder.HasOne(c => c.Empresa)
            .WithMany()
            .HasForeignKey(c => c.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);

        // ProdutoFinal: navegacao com WithMany(p => p.Composicoes)
        builder.HasOne(c => c.ProdutoFinal)
            .WithMany(p => p.Composicoes)
            .HasForeignKey(c => c.ProdutoFinalId)
            .OnDelete(DeleteBehavior.Restrict);

        // Insumo: sem navegacao reversa (Produto nao precisa enumerar onde e usado como insumo)
        builder.HasOne(c => c.Insumo)
            .WithMany()
            .HasForeignKey(c => c.InsumoId)
            .OnDelete(DeleteBehavior.Restrict);

        // Loja: opcional
        builder.HasOne(c => c.Loja)
            .WithMany()
            .HasForeignKey(c => c.LojaId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Chave unique: (Empresa, ProdutoFinal, Insumo, Loja). LojaId null = receita padrao;
        // preenchido = override por loja. Permite ambas coexistir.
        builder.HasIndex(c => new { c.EmpresaId, c.ProdutoFinalId, c.InsumoId, c.LojaId })
            .IsUnique()
            .HasDatabaseName("ix_produtos_composicao_empresa_final_insumo_loja_unique");

        // Lookup primario da calculadora: dado um produto-final, traz suas linhas
        builder.HasIndex(c => new { c.EmpresaId, c.ProdutoFinalId })
            .HasDatabaseName("ix_produtos_composicao_empresa_final");

        // Busca reversa "onde este produto e usado como insumo"
        builder.HasIndex(c => new { c.EmpresaId, c.InsumoId })
            .HasDatabaseName("ix_produtos_composicao_empresa_insumo");
    }
}
