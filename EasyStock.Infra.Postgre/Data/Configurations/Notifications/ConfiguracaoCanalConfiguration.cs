using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Infra.Postgre.Data.Configurations.Notifications;

public class ConfiguracaoCanalConfiguration : IEntityTypeConfiguration<ConfiguracaoCanal>
{
    public void Configure(EntityTypeBuilder<ConfiguracaoCanal> b)
    {
        b.ToTable("notif_configuracoes_canal");
        b.HasKey(x => x.Id);

        b.Property(x => x.EmpresaId);
        b.Property(x => x.Canal).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.ProviderAtivo).HasMaxLength(40).IsRequired();
        b.Property(x => x.CredenciaisCifradas);
        b.Property(x => x.LimiteDiarioPorUsuario);
        b.Property(x => x.JanelaPermitidaInicio);
        b.Property(x => x.JanelaPermitidaFim);
        b.Property(x => x.AtivoNoTenant).IsRequired();
        b.Property(x => x.AtualizadoEm).IsRequired();
        b.Property(x => x.AtualizadoPor).HasMaxLength(256).IsRequired();

        b.HasIndex(x => new { x.EmpresaId, x.Canal }).IsUnique();

        b.HasOne(x => x.Empresa)
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
