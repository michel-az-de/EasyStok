namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class AdminImpersonationLogConfiguration : IEntityTypeConfiguration<AdminImpersonationLog>
    {
        public void Configure(EntityTypeBuilder<AdminImpersonationLog> builder)
        {
            builder.ToTable("admin_impersonation_logs");
            builder.HasKey(l => l.Id);
            builder.Property(l => l.Ip).IsRequired().HasMaxLength(50);

            builder.HasOne(l => l.AdminUsuario)
                .WithMany()
                .HasForeignKey(l => l.AdminUsuarioId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(l => l.Empresa)
                .WithMany()
                .HasForeignKey(l => l.EmpresaId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
