using EasyStock.Domain.ValueObjects.Pix;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class LojaConfiguration : IEntityTypeConfiguration<Loja>
    {
        public void Configure(EntityTypeBuilder<Loja> builder)
        {
            builder.ToTable("lojas");
            builder.HasKey(l => l.Id);
            builder.Property(l => l.Nome).IsRequired().HasMaxLength(150);
            builder.Property(l => l.Descricao).HasMaxLength(500);
            builder.Property(l => l.Documento).HasMaxLength(30);
            builder.Property(l => l.Endereco).HasMaxLength(300);
            builder.Property(l => l.Telefone).HasMaxLength(30);
            builder.Property(l => l.LogoUrl).HasMaxLength(500);
            // #2233: chave Pix da loja (Pix direto). Herda a RLS de lojas.
            builder.Property(l => l.PixTipo).HasConversion<int?>();
            builder.Property(l => l.PixChave).HasMaxLength(ChavePix.TamanhoMaximo);
            builder.Property(l => l.PixNomeRecebedor).HasMaxLength(RecebedorPix.NomeTamanhoMaximo);
            builder.Property(l => l.PixCidade).HasMaxLength(RecebedorPix.CidadeTamanhoMaximo);

            builder.HasOne(l => l.Empresa)
                .WithMany(e => e.Lojas)
                .HasForeignKey(l => l.EmpresaId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(l => new { l.EmpresaId, l.Ativa });
        }
    }
}
