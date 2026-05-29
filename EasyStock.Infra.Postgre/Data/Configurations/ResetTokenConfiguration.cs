namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class ResetTokenConfiguration : IEntityTypeConfiguration<ResetToken>
    {
        public void Configure(EntityTypeBuilder<ResetToken> builder)
        {
            builder.ToTable("reset_tokens");

            builder.HasKey(rt => rt.Id);

            builder.Property(rt => rt.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid");

            builder.Property(rt => rt.UsuarioId)
                .HasColumnType("uuid");

            builder.Property(rt => rt.TokenHash)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnType("character varying(128)");

            builder.Property(rt => rt.CriadoEm)
                .HasColumnType("timestamp with time zone");

            builder.Property(rt => rt.ExpiraEm)
                .HasColumnType("timestamp with time zone");

            builder.Property(rt => rt.Usado)
                .HasColumnType("boolean");

            builder.Property(rt => rt.IpCriacao)
                .HasMaxLength(45)
                .HasColumnType("character varying(45)");

            builder.Property(rt => rt.UserAgent)
                .HasMaxLength(500)
                .HasColumnType("character varying(500)");

            builder.HasOne(rt => rt.Usuario)
                .WithMany()
                .HasForeignKey(rt => rt.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unique evita reuso de token e habilita lookup por hash O(1).
            builder.HasIndex(rt => rt.TokenHash)
                .IsUnique()
                .HasDatabaseName("ux_reset_tokens_token_hash");
            builder.HasIndex(rt => rt.UsuarioId);
            // Cleanup job consulta apenas tokens ainda válidos.
            builder.HasIndex(rt => rt.ExpiraEm)
                .HasFilter("\"Usado\" = false")
                .HasDatabaseName("ix_reset_tokens_expira_pendente");
        }
    }
}