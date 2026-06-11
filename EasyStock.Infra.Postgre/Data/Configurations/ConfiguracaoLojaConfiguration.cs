using EasyStock.Domain.Defaults;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class ConfiguracaoLojaConfiguration : IEntityTypeConfiguration<ConfiguracaoLoja>
{
    public void Configure(EntityTypeBuilder<ConfiguracaoLoja> builder)
    {
        builder.ToTable("configuracoes_loja");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DiasAlertaValidade).HasDefaultValue(OperacionalDefaults.DiasAlertaValidade);
        builder.Property(x => x.DiasAlertaParado).HasDefaultValue(OperacionalDefaults.DiasAlertaParado);
        builder.Property(x => x.QuantidadeMinimaPadrao).HasDefaultValue(OperacionalDefaults.QuantidadeMinima);
        builder.Property(x => x.QuantidadeCriticaPadrao).HasDefaultValue(OperacionalDefaults.QuantidadeCritica);
        builder.Property(x => x.NotificarEstoqueCritico).HasDefaultValue(true);
        builder.Property(x => x.NotificarValidade).HasDefaultValue(true);
        builder.Property(x => x.NotificarParado).HasDefaultValue(true);
        builder.Property(x => x.NotificarReposicao).HasDefaultValue(true);
        builder.Property(x => x.FifoAtivo).HasDefaultValue(true);
        builder.Property(x => x.KdsHabilitado).HasDefaultValue(false);
        builder.Property(x => x.Moeda).HasMaxLength(10).HasDefaultValue(OperacionalDefaults.Moeda);
        builder.Property(x => x.Timezone).HasMaxLength(100).HasDefaultValue(OperacionalDefaults.Timezone);

        builder.HasIndex(x => x.LojaId).IsUnique();
        builder.HasOne(x => x.Loja)
            .WithMany()
            .HasForeignKey(x => x.LojaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
