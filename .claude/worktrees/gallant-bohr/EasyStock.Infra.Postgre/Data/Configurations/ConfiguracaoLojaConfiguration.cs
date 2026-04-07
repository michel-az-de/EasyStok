using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class ConfiguracaoLojaConfiguration : IEntityTypeConfiguration<ConfiguracaoLoja>
{
    public void Configure(EntityTypeBuilder<ConfiguracaoLoja> builder)
    {
        builder.ToTable("configuracoes_loja");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DiasAlertaValidade).HasDefaultValue(15);
        builder.Property(x => x.DiasAlertaParado).HasDefaultValue(30);
        builder.Property(x => x.QuantidadeMinimaPadrao).HasDefaultValue(5);
        builder.Property(x => x.NotificarEstoqueCritico).HasDefaultValue(true);
        builder.Property(x => x.NotificarValidade).HasDefaultValue(true);
        builder.Property(x => x.NotificarParado).HasDefaultValue(true);
        builder.Property(x => x.NotificarReposicao).HasDefaultValue(true);
        builder.Property(x => x.FifoAtivo).HasDefaultValue(true);
        builder.Property(x => x.Moeda).HasMaxLength(10).HasDefaultValue("BRL");
        builder.Property(x => x.Timezone).HasMaxLength(100).HasDefaultValue("America/Sao_Paulo");

        builder.HasIndex(x => x.LojaId).IsUnique();
        builder.HasOne(x => x.Loja)
            .WithMany()
            .HasForeignKey(x => x.LojaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
