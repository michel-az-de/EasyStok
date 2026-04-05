using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    internal sealed class AnuncioIaConfiguration : IEntityTypeConfiguration<AnuncioIa>
    {
        public void Configure(EntityTypeBuilder<AnuncioIa> builder)
        {
            builder.ToTable("anuncios_ia");

            builder.HasKey(a => a.Id);

            builder.Property(a => a.EmpresaId).IsRequired();
            builder.Property(a => a.ProdutoId).IsRequired();
            builder.Property(a => a.ProdutoVariacaoId);
            builder.Property(a => a.Titulo).IsRequired().HasMaxLength(255);
            builder.Property(a => a.Conteudo).IsRequired().HasColumnType("text");
            builder.Property(a => a.InstrucoesUsadas).HasColumnType("text");
            builder.Property(a => a.TokensConsumidos).IsRequired();
            builder.Property(a => a.Salvo).IsRequired();
            builder.Property(a => a.CriadoEm).IsRequired();

            builder.HasIndex(a => new { a.EmpresaId, a.ProdutoId });
            builder.HasIndex(a => new { a.EmpresaId, a.CriadoEm });

            builder.HasOne(a => a.Produto)
                .WithMany()
                .HasForeignKey(a => a.ProdutoId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
