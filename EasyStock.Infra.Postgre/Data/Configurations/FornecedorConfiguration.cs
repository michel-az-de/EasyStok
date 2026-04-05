using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class FornecedorConfiguration : IEntityTypeConfiguration<Fornecedor>
    {
        public void Configure(EntityTypeBuilder<Fornecedor> builder)
        {
            builder.ToTable("fornecedores");
            builder.HasKey(f => f.Id);
            builder.Property(f => f.Nome).IsRequired().HasMaxLength(150);
            builder.Property(f => f.Documento).HasMaxLength(30);
            builder.Property(f => f.Email).HasMaxLength(150);
            builder.Property(f => f.Telefone).HasMaxLength(30);
            builder.Property(f => f.Contato).HasMaxLength(150);
            builder.Property(f => f.Categoria).HasMaxLength(120);
            builder.Property(f => f.Tipo).HasMaxLength(60);
            builder.Property(f => f.LeadTimeEstimadoDias);
            builder.Property(f => f.LeadTimeRealMedioDias).HasColumnType("decimal(10,2)");
            builder.Property(f => f.SiteUrl).HasMaxLength(255);
            builder.Property(f => f.PedidoMinimo).HasMaxLength(120);
            builder.Property(f => f.FretePadrao).HasMaxLength(120);
            builder.Property(f => f.Observacoes).HasColumnType("text");

            builder.HasOne(f => f.Empresa)
                .WithMany()
                .HasForeignKey(f => f.EmpresaId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(f => new { f.EmpresaId, f.Ativo });
        }
    }
}
