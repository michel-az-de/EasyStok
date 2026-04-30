using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class MovimentoCaixaConfiguration : IEntityTypeConfiguration<MovimentoCaixa>
    {
        public void Configure(EntityTypeBuilder<MovimentoCaixa> b)
        {
            b.ToTable("movimentos_caixa");
            b.HasKey(x => x.Id);
            b.Property(x => x.Tipo).IsRequired().HasMaxLength(20);
            b.Property(x => x.Valor).HasColumnType("numeric(14,2)");
            b.Property(x => x.Descricao).HasColumnType("text");
            b.Property(x => x.Metodo).HasMaxLength(20);
            b.Property(x => x.Categoria).HasMaxLength(60);
            b.Property(x => x.Referencia).HasMaxLength(120);
            b.Property(x => x.RegistradoPorNome).HasMaxLength(120);
            b.Property(x => x.Origem).HasMaxLength(20);
            b.Property(x => x.EstornadoPorNome).HasMaxLength(120);
            b.Property(x => x.MotivoEstorno).HasColumnType("text");

            b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Loja).WithMany().HasForeignKey(x => x.LojaId).OnDelete(DeleteBehavior.SetNull);

            // Ignore prop derivada
            b.Ignore(x => x.Ativo);
            b.Ignore(x => x.SinalNoCaixa);
        }
    }

    public class FechamentoCaixaConfiguration : IEntityTypeConfiguration<FechamentoCaixa>
    {
        public void Configure(EntityTypeBuilder<FechamentoCaixa> b)
        {
            b.ToTable("fechamentos_caixa");
            b.HasKey(x => x.Id);
            b.Property(x => x.SaldoInicial).HasColumnType("numeric(14,2)");
            b.Property(x => x.TotalVendas).HasColumnType("numeric(14,2)");
            b.Property(x => x.TotalPagamentosPedidos).HasColumnType("numeric(14,2)");
            b.Property(x => x.TotalEntradasExtras).HasColumnType("numeric(14,2)");
            b.Property(x => x.TotalSaidasExtras).HasColumnType("numeric(14,2)");
            b.Property(x => x.SaldoFinal).HasColumnType("numeric(14,2)");
            b.Property(x => x.FechadoPorNome).HasMaxLength(120);
            b.Property(x => x.Observacoes).HasColumnType("text");

            b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Loja).WithMany().HasForeignKey(x => x.LojaId).OnDelete(DeleteBehavior.SetNull);

            b.HasIndex(x => new { x.EmpresaId, x.LojaId, x.Data }).IsUnique();
        }
    }
}
