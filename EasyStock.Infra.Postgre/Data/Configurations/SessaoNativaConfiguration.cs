namespace EasyStock.Infra.Postgre.Data.Configurations;

public sealed class SessaoNativaConfiguration : IEntityTypeConfiguration<SessaoNativa>
{
    public void Configure(EntityTypeBuilder<SessaoNativa> builder)
    {
        builder.ToTable("sessoes_nativas");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.DispositivoId).HasMaxLength(64).IsRequired();
        builder.Property(s => s.CriadaEm).HasColumnType("timestamp with time zone");
        builder.Property(s => s.RevogadaEm).HasColumnType("timestamp with time zone");
        builder.HasIndex(s => new { s.UsuarioId, s.DispositivoId });
        builder.HasOne<Usuario>().WithMany().HasForeignKey(s => s.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Empresa>().WithMany().HasForeignKey(s => s.EmpresaId).OnDelete(DeleteBehavior.Restrict);
    }
}
