using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class FornecedorAlteracaoConfiguration : IEntityTypeConfiguration<FornecedorAlteracao>
    {
        public void Configure(EntityTypeBuilder<FornecedorAlteracao> b)
        {
            b.ToTable("fornecedor_alteracoes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Campo).IsRequired().HasMaxLength(60);
            b.Property(x => x.ValorAntigo).HasColumnType("text");
            b.Property(x => x.ValorNovo).HasColumnType("text");
            b.Property(x => x.AlteradoPorNome).HasMaxLength(120);
            b.Property(x => x.Origem).HasMaxLength(20);

            b.HasOne(x => x.Fornecedor)
                .WithMany()
                .HasForeignKey(x => x.FornecedorId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => new { x.FornecedorId, x.AlteradoEm });
        }
    }

    public class VendaAlteracaoConfiguration : IEntityTypeConfiguration<VendaAlteracao>
    {
        public void Configure(EntityTypeBuilder<VendaAlteracao> b)
        {
            b.ToTable("venda_alteracoes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Campo).IsRequired().HasMaxLength(60);
            b.Property(x => x.ValorAntigo).HasColumnType("text");
            b.Property(x => x.ValorNovo).HasColumnType("text");
            b.Property(x => x.AlteradoPorNome).HasMaxLength(120);
            b.Property(x => x.Origem).HasMaxLength(20);

            b.HasOne(x => x.Venda)
                .WithMany()
                .HasForeignKey(x => x.VendaId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => new { x.VendaId, x.AlteradoEm });
        }
    }
}
