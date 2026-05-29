using System.Text.Json;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Infra.Postgre.Data.Configurations.Fiscal;

public class EmpresaConfiguracaoFiscalConfiguration : IEntityTypeConfiguration<EmpresaConfiguracaoFiscal>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public void Configure(EntityTypeBuilder<EmpresaConfiguracaoFiscal> builder)
    {
        builder.ToTable("empresa_configuracao_fiscal");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.RegimeTributario).HasConversion<string>().IsRequired().HasMaxLength(40);
        builder.Property(c => c.Ambiente).HasConversion<string>().IsRequired().HasMaxLength(20);

        builder.Property(c => c.InscricaoEstadual).HasMaxLength(20);
        builder.Property(c => c.InscricaoMunicipal).HasMaxLength(20);

        builder.Property(c => c.ProvedorPreferido).IsRequired().HasMaxLength(20).HasDefaultValue("mock");
        builder.Property(c => c.SerieNfce).HasDefaultValue((short)1);
        builder.Property(c => c.ProximoNumeroNfce).HasDefaultValue(1L);
        builder.Property(c => c.Habilitada).HasDefaultValue(false);

        builder.Property(c => c.CscId).HasMaxLength(10);
        builder.Property(c => c.CscToken).HasMaxLength(100);

        // Endereco completo do emitente como jsonb (espelha padrao Fatura.DadosEmissor).
        builder.Property(c => c.Endereco)
            .HasColumnName("endereco")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonOpts),
                v => v == null ? null : JsonSerializer.Deserialize<Endereco>(v, JsonOpts));

        // Optimistic concurrency via xmin (Postgres system column) — protege ReservarProximoNumero.
        builder.Property(c => c.Versao)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasOne(c => c.Empresa)
            .WithMany()
            .HasForeignKey(c => c.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);

        // 1:1 com Empresa: unique em EmpresaId.
        builder.HasIndex(c => c.EmpresaId)
            .IsUnique()
            .HasDatabaseName("ux_empresa_configuracao_fiscal_empresa");

        // Index para localizar credencial vinculada (rotina de rotacao KEK).
        builder.HasIndex(c => c.CertificadoCredencialId)
            .HasDatabaseName("ix_empresa_configuracao_fiscal_certificado")
            .HasFilter("\"CertificadoCredencialId\" IS NOT NULL");
    }
}
