using EasyStock.Domain.Entities.Banners;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    /// <summary>
    /// Interação de usuário com banner (#869). Tabela GLOBAL, sem <c>EmpresaId</c> (molde
    /// FaqVisualizacao): a confirmação vale independente da empresa ativa da sessão, senão
    /// o banner obrigatório reapareceria ao trocar de empresa. Idempotência via índice
    /// único (BannerId, UsuarioId, Tipo). FK Restrict preserva a prova de recebimento.
    /// </summary>
    public sealed class BannerConfirmacaoConfiguration : IEntityTypeConfiguration<BannerConfirmacao>
    {
        public void Configure(EntityTypeBuilder<BannerConfirmacao> builder)
        {
            builder.ToTable("banner_confirmacoes");
            builder.HasKey(c => c.Id);

            builder.Property(c => c.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(c => c.RegistradoEm).HasColumnType("timestamp with time zone").IsRequired();

            builder.HasOne(c => c.Banner)
                .WithMany()
                .HasForeignKey(c => c.BannerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Idempotência: um usuário registra cada tipo de interação no máximo uma vez por banner.
            builder.HasIndex(c => new { c.BannerId, c.UsuarioId, c.Tipo })
                .IsUnique()
                .HasDatabaseName("ux_banner_confirmacoes_banner_usuario_tipo");

            // Acelera o NOT EXISTS da query de "ativos não-confirmados por usuário".
            builder.HasIndex(c => new { c.UsuarioId, c.BannerId })
                .HasDatabaseName("ix_banner_confirmacoes_usuario");
        }
    }
}
