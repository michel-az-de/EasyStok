using EasyStock.Domain.Entities.Financeiro;

namespace EasyStock.Infra.Postgre.Data.Configurations;

public class ParcelaReceberConfiguration : IEntityTypeConfiguration<ParcelaReceber>
{
    public void Configure(EntityTypeBuilder<ParcelaReceber> b)
    {
        b.ToTable("parcelas_receber");
        b.HasKey(x => x.Id);

        b.Property(x => x.Numero).IsRequired();
        b.Property(x => x.Valor).HasColumnType("numeric(14,2)");
        b.Property(x => x.ValorPago).HasColumnType("numeric(14,2)").HasDefaultValue(0m);
        b.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(30);
        b.Property(x => x.MetodoPlanejado).HasMaxLength(20);

        b.Property(x => x.EfiTxid).HasMaxLength(120);
        b.Property(x => x.PixCopiaCola).HasColumnType("text");
        b.Property(x => x.QrCodeBase64).HasColumnType("text");

        b.Property(x => x.Versao)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        b.HasMany(x => x.Pagamentos)
            .WithOne(p => p.ParcelaReceber)
            .HasForeignKey(p => p.ParcelaReceberId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.ContaReceberId, x.Numero }).IsUnique().HasDatabaseName("ux_parcelas_receber_conta_numero");
        b.HasIndex(x => new { x.EmpresaId, x.DataVencimento, x.Status }).HasDatabaseName("ix_parcelas_receber_empresa_vencimento_status");
        b.HasIndex(x => new { x.EmpresaId, x.Status }).HasDatabaseName("ix_parcelas_receber_empresa_status");

        // Pix txid global UNIQUE filtrado (cross-tenant pra webhook resolver)
        b.HasIndex(x => x.EfiTxid)
            .IsUnique()
            .HasDatabaseName("ux_parcelas_receber_efi_txid")
            .HasFilter("\"EfiTxid\" IS NOT NULL");

        b.Ignore(x => x.Saldo);
    }
}
