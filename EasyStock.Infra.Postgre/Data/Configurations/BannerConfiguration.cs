using EasyStock.Domain.Entities.Banners;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    /// <summary>
    /// Banner de broadcast global (#869). Tabela SEM <c>EmpresaId</c> — logo isenta do
    /// filtro multi-tenant do EF e do RLS (ver EasyStockDbContext.ApplyTenantQueryFilters).
    /// </summary>
    public sealed class BannerConfiguration : IEntityTypeConfiguration<Banner>
    {
        public void Configure(EntityTypeBuilder<Banner> builder)
        {
            builder.ToTable("banners");
            builder.HasKey(b => b.Id);

            builder.Property(b => b.TituloInterno).IsRequired().HasMaxLength(120);
            builder.Property(b => b.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(b => b.Corpo).HasMaxLength(4000);
            builder.Property(b => b.ImagemStorageKey).HasMaxLength(500);
            builder.Property(b => b.ImagemUrl).HasMaxLength(1000);
            builder.Property(b => b.LinkUrl).HasMaxLength(2000);
            builder.Property(b => b.TooltipTexto).HasMaxLength(300);
            builder.Property(b => b.TamanhoModo).HasConversion<string>().HasMaxLength(20).IsRequired();

            builder.Property(b => b.InicioEm).HasColumnType("timestamp with time zone");
            builder.Property(b => b.FimEm).HasColumnType("timestamp with time zone");
            builder.Property(b => b.NotificadoEm).HasColumnType("timestamp with time zone");
            builder.Property(b => b.CriadoEm).HasColumnType("timestamp with time zone").IsRequired();
            builder.Property(b => b.AtualizadoEm).HasColumnType("timestamp with time zone").IsRequired();

            // Índice parcial para a query de "ativos por data" (§1.3 do plano).
            builder.HasIndex(b => new { b.Ativo, b.InicioEm, b.FimEm })
                .HasDatabaseName("ix_banners_ativos")
                .HasFilter("\"Ativo\" = TRUE");

            builder.HasIndex(b => b.Prioridade)
                .HasDatabaseName("ix_banners_prioridade");
        }
    }
}
