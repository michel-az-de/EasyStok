namespace EasyStock.Infra.Postgre.Data.Configurations;

public class SystemErrorLogConfiguration : IEntityTypeConfiguration<SystemErrorLog>
{
    public void Configure(EntityTypeBuilder<SystemErrorLog> builder)
    {
        builder.ToTable("system_error_logs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Source).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Level).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Category).HasMaxLength(60);
        builder.Property(x => x.Message).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.Details).HasMaxLength(8000);
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.Property(x => x.Url).HasMaxLength(500);
        builder.Property(x => x.AdminEmail).HasMaxLength(256);

        builder.HasIndex(x => x.CriadoEm);
        builder.HasIndex(x => new { x.Source, x.Level });
    }
}
