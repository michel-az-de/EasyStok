using System.Text.Json;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EasyStock.Infra.Postgre.Data.Configurations.Atendimento;

public class ConfiguracaoAtendimentoConfiguration : IEntityTypeConfiguration<ConfiguracaoAtendimento>
{
    private static readonly JsonSerializerOptions AvisosJson = new(JsonSerializerDefaults.Web);

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
        // #1427: SLA de primeira resposta da loja; linha antiga ganha o padrão.
        builder.Property(x => x.SlaRespostaMinutos).HasDefaultValue(ConfiguracaoAtendimento.SlaRespostaPadraoMinutos);
        // S58: modelo de retomada fora da janela de 24 h (nulo = sem retomada).
        builder.Property(x => x.ModeloRetomadaNome).HasMaxLength(ConfiguracaoAtendimento.ModeloNomeTamanhoMaximo);
        builder.Property(x => x.ModeloRetomadaIdioma).IsRequired().HasMaxLength(16)
            .HasDefaultValue(ConfiguracaoAtendimento.IdiomaModeloPadrao);
        // #2096: avisos da operação num documento só; linha antiga ('{}') lê o padrão de cada campo.
        builder.Property(x => x.Avisos)
            .HasColumnName("avisos_operacao")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValueSql("'{}'::jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, AvisosJson),
                v => JsonSerializer.Deserialize<AvisosOperacao>(v, AvisosJson) ?? AvisosOperacao.Padrao,
                new ValueComparer<AvisosOperacao>((a, b) => a == b, v => v.GetHashCode(), v => v));
        builder.Property(x => x.CriadoEm).IsRequired();
        builder.Property(x => x.AlteradoEm).IsRequired();

        builder.HasOne<Domain.Entities.Empresa>()
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
