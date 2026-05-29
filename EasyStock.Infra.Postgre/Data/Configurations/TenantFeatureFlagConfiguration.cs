namespace EasyStock.Infra.Postgre.Data.Configurations;

public class TenantFeatureFlagConfiguration : IEntityTypeConfiguration<TenantFeatureFlag>
{
    public void Configure(EntityTypeBuilder<TenantFeatureFlag> b)
    {
        b.ToTable("TenantFeatureFlags");
        b.HasKey(x => x.Id);
        b.Property(x => x.Feature).HasMaxLength(50).IsRequired();
        b.Property(x => x.AlteradoPor).HasMaxLength(256).IsRequired();
        b.HasIndex(x => new { x.EmpresaId, x.Feature }).IsUnique();
    }
}
