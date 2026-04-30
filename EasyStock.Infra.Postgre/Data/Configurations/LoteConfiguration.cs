using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class LoteConfiguration : IEntityTypeConfiguration<Lote>
    {
        public void Configure(EntityTypeBuilder<Lote> b)
        {
            b.ToTable("lotes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Codigo).IsRequired().HasMaxLength(40);
            b.Property(x => x.Status).IsRequired().HasMaxLength(20);
            b.Property(x => x.OperadorNome).HasMaxLength(120);
            b.Property(x => x.Observacoes).HasColumnType("text");
            b.Property(x => x.FotoUrl).HasColumnType("text");
            b.Property(x => x.Origem).HasMaxLength(20);
            b.Property(x => x.MobileBatchId).HasMaxLength(64);

            b.HasIndex(x => new { x.EmpresaId, x.Codigo }).IsUnique();

            b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Loja).WithMany().HasForeignKey(x => x.LojaId).OnDelete(DeleteBehavior.SetNull);

            b.HasMany(x => x.Itens).WithOne(i => i.Lote!).HasForeignKey(i => i.LoteId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Etiquetas).WithOne(e => e.Lote!).HasForeignKey(e => e.LoteId).OnDelete(DeleteBehavior.Cascade);

            b.Ignore(x => x.EstaFinalizado);
            b.Ignore(x => x.TotalUnidades);
        }
    }

    public class LoteItemConfiguration : IEntityTypeConfiguration<LoteItem>
    {
        public void Configure(EntityTypeBuilder<LoteItem> b)
        {
            b.ToTable("lote_itens");
            b.HasKey(x => x.Id);
            b.Property(x => x.Nome).IsRequired().HasMaxLength(150);
            b.Property(x => x.Emoji).HasMaxLength(16);
            b.Property(x => x.Unidade).HasMaxLength(32);
            b.Property(x => x.FotoUrl).HasColumnType("text");

            b.HasOne(x => x.Produto).WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.SetNull);
        }
    }

    public class LoteEtiquetaConfiguration : IEntityTypeConfiguration<LoteEtiqueta>
    {
        public void Configure(EntityTypeBuilder<LoteEtiqueta> b)
        {
            b.ToTable("lote_etiquetas");
            b.HasKey(x => x.Id);
            b.Property(x => x.Codigo).IsRequired().HasMaxLength(60);
            b.Property(x => x.Status).IsRequired().HasMaxLength(20);
            b.Property(x => x.ConferidaPorNome).HasMaxLength(120);
            b.Property(x => x.ObservacaoConferencia).HasColumnType("text");

            b.HasIndex(x => x.Codigo).IsUnique();
            b.HasIndex(x => new { x.LoteId, x.Status });

            b.HasOne(x => x.LoteItem).WithMany().HasForeignKey(x => x.LoteItemId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
