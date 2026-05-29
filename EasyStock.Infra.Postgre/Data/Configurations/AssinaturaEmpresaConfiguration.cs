namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class AssinaturaEmpresaConfiguration : IEntityTypeConfiguration<AssinaturaEmpresa>
    {
        public void Configure(EntityTypeBuilder<AssinaturaEmpresa> builder)
        {
            builder.ToTable("assinaturas_empresa");
            builder.HasKey(a => a.Id);
            builder.Property(a => a.Status).HasConversion<string>().IsRequired().HasMaxLength(50);
            builder.Property(a => a.CupomCodigo).HasMaxLength(50);
            builder.Property(a => a.DescontoAplicado).HasColumnType("decimal(10,2)");
            builder.Ignore(a => a.TrialAtivo);
            builder.HasOne(a => a.Empresa).WithMany().HasForeignKey(a => a.EmpresaId);
            builder.HasOne(a => a.Plano).WithMany().HasForeignKey(a => a.PlanoId);
        }
    }
}
