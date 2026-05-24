using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

public class ClienteOtpConfiguration : IEntityTypeConfiguration<ClienteOtp>
{
    public void Configure(EntityTypeBuilder<ClienteOtp> builder)
    {
        builder.ToTable("cliente_otp");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.EmpresaId).IsRequired();

        // SHA-256 hex = 64 chars sempre.
        builder.Property(o => o.TelefoneHash)
            .IsRequired()
            .HasMaxLength(64);

        // BCrypt $2a$$10$... ~60 chars; folga até 100.
        builder.Property(o => o.CodigoHash)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(o => o.ExpiraEm).IsRequired();
        builder.Property(o => o.Tentativas).IsRequired().HasDefaultValue(0);
        builder.Property(o => o.Consumido).IsRequired().HasDefaultValue(false);

        // IPv6 max 45 chars; IPv4 mapped ("::ffff:192.0.2.1") cabe.
        builder.Property(o => o.IpOrigem).HasMaxLength(45);
        builder.Property(o => o.UserAgent).HasMaxLength(300);

        builder.Property(o => o.CriadoEm).IsRequired();

        // FK Empresa — CASCADE: deleção de empresa limpa OTPs.
        // OTP não tem FK Cliente: o cliente pode nem existir no momento da emissão
        // (lookup por TelefoneHash + EmpresaId resolve o ClienteId depois).
        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(o => o.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);

        // Lookup principal: "qual OTP ativo para este telefone nesta empresa?"
        // (EmpresaId, TelefoneHash, ExpiraEm) cobre query típica do solicitar/validar.
        builder.HasIndex(o => new { o.EmpresaId, o.TelefoneHash, o.ExpiraEm })
            .HasDatabaseName("ix_cliente_otp_empresa_telefone_expira");

        // Job de limpeza periódica filtra por ExpiraEm.
        builder.HasIndex(o => o.ExpiraEm)
            .HasDatabaseName("ix_cliente_otp_expira");
    }
}
