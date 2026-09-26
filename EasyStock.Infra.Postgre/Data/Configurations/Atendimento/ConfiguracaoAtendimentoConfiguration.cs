using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public class ConfiguracaoAtendimentoConfiguration : IEntityTypeConfiguration<ConfiguracaoAtendimento>
{
    public void Configure(EntityTypeBuilder<ConfiguracaoAtendimento> builder)
    {
        builder.ToTable("configuracoes_atendimento");
        builder.HasKey(x => x.EmpresaId);

        builder.Property(x => x.Tom).IsRequired().HasMaxLength(200);
        builder.Property(x => x.SaudacaoPrimeiroContato).IsRequired().HasMaxLength(500);
        builder.Property(x => x.SaudacaoRetorno).IsRequired().HasMaxLength(500);
        builder.Property(x => x.FraseEspera).IsRequired().HasMaxLength(200);
        builder.Property(x => x.MensagemForaArea).IsRequired().HasMaxLength(500);
        builder.Property(x => x.NivelSugestao).HasConversion<int>();
        builder.Property(x => x.RespiroMinutos).HasDefaultValue(40);
        builder.Property(x => x.TempoPreparoPadraoMinutos).HasDefaultValue(60);
        builder.Property(x => x.Ativo).HasDefaultValue(true);
        builder.Property(x => x.CriadoEm).IsRequired();
        builder.Property(x => x.AlteradoEm).IsRequired();

        builder.HasOne<Domain.Entities.Empresa>()
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
