using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class ListaComprasConfiguration : IEntityTypeConfiguration<ListaCompras>
    {
        public void Configure(EntityTypeBuilder<ListaCompras> b)
        {
            b.ToTable("listas_compras");
            b.HasKey(x => x.Id);
            b.Property(x => x.Nome).IsRequired().HasMaxLength(120);
            b.Property(x => x.Status).IsRequired().HasMaxLength(20);
            b.Property(x => x.Observacoes).HasColumnType("text");
            b.Property(x => x.CriadaPorNome).HasMaxLength(120);
            b.Property(x => x.Origem).HasMaxLength(20);

            b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Loja).WithMany().HasForeignKey(x => x.LojaId).OnDelete(DeleteBehavior.SetNull);

            b.HasMany(x => x.Itens).WithOne(i => i.ListaCompras!)
                .HasForeignKey(i => i.ListaComprasId).OnDelete(DeleteBehavior.Cascade);

            b.Ignore(x => x.TotalItens);
            b.Ignore(x => x.ItensFeitos);
            b.Ignore(x => x.ItensPendentes);
            b.Ignore(x => x.EstaArquivada);
        }
    }

    public class ItemListaComprasConfiguration : IEntityTypeConfiguration<ItemListaCompras>
    {
        public void Configure(EntityTypeBuilder<ItemListaCompras> b)
        {
            b.ToTable("itens_lista_compras");
            b.HasKey(x => x.Id);
            b.Property(x => x.Texto).IsRequired().HasMaxLength(255);
            b.Property(x => x.Quantidade).HasColumnType("numeric(14,3)");
            b.Property(x => x.Unidade).HasMaxLength(32);
            b.Property(x => x.Observacao).HasColumnType("text");
            b.Property(x => x.Categoria).HasMaxLength(60);
            b.Property(x => x.DonePorNome).HasMaxLength(120);
        }
    }
}
