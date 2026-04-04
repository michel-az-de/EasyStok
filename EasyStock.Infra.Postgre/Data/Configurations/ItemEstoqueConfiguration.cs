using EasyStok.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class ItemEstoqueConfiguration : IEntityTypeConfiguration<ItemEstoque>
    {
        public void Configure(EntityTypeBuilder<ItemEstoque> builder)
        {
            builder.ToTable("itens_estoque");
            builder.HasKey(i => i.Id);
            builder.Property(i => i.CodigoInterno).HasMaxLength(120);
            builder.Property(i => i.CodigoLote).HasMaxLength(120);
            builder.Property(i => i.CodigoMarketplace).HasMaxLength(120);
            builder.Property(i => i.VariacaoDescricao).HasMaxLength(180);
            builder.Property(i => i.Cor).HasMaxLength(60);
            builder.Property(i => i.Tamanho).HasMaxLength(60);
            builder.Property(i => i.Status).IsRequired().HasMaxLength(50);

            builder.Property(i => i.PesoReal).HasColumnType("decimal(10,3)");
            builder.Property(i => i.LarguraReal).HasColumnType("decimal(10,2)");
            builder.Property(i => i.AlturaReal).HasColumnType("decimal(10,2)");
            builder.Property(i => i.ComprimentoReal).HasColumnType("decimal(10,2)");

            builder.Property(i => i.CustoUnitario).HasColumnType("decimal(18,2)");
            builder.Property(i => i.PrecoVendaSugerido).HasColumnType("decimal(18,2)");

            builder.HasOne(i => i.Empresa).WithMany(e => e.ItensEstoque).HasForeignKey(i => i.EmpresaId);
            builder.HasOne(i => i.Produto).WithMany(p => p.ItensEstoque).HasForeignKey(i => i.ProdutoId);
        }
    }
}
