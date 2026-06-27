namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class AjusteInventarioConfiguration : IEntityTypeConfiguration<AjusteInventario>
    {
        public void Configure(EntityTypeBuilder<AjusteInventario> builder)
        {
            builder.ToTable("ajustes_inventario");
            builder.HasKey(a => a.Id);

            // Agregados derivados das linhas — nunca persistidos.
            builder.Ignore(a => a.TotalMutados);
            builder.Ignore(a => a.TotalCriados);
            builder.Ignore(a => a.TotalZerados);
            builder.Ignore(a => a.CustoTotalPerda);

            builder.HasOne(a => a.Empresa).WithMany()
                .HasForeignKey(a => a.EmpresaId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(a => a.Contagem).WithMany()
                .HasForeignKey(a => a.ContagemId).OnDelete(DeleteBehavior.Restrict);
            builder.HasMany(a => a.Linhas).WithOne(l => l.AjusteInventario)
                .HasForeignKey(l => l.AjusteInventarioId).OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(a => a.ContagemId).HasDatabaseName("ix_ajustes_inventario_contagem");
        }
    }
}
