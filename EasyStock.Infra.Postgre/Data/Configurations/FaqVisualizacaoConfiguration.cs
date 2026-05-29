namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public sealed class FaqVisualizacaoConfiguration : IEntityTypeConfiguration<FaqVisualizacao>
    {
        public void Configure(EntityTypeBuilder<FaqVisualizacao> builder)
        {
            builder.ToTable("faq_visualizacoes");
            builder.HasKey(v => v.Id);

            builder.Property(v => v.IpHash).IsRequired().HasMaxLength(64);
            builder.Property(v => v.Termo).HasMaxLength(200);
            builder.Property(v => v.Origem).HasMaxLength(40);

            builder.HasOne(v => v.Item)
                .WithMany()
                .HasForeignKey(v => v.ItemId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(v => new { v.ItemId, v.CriadoEm })
                .HasDatabaseName("ix_faq_visualizacoes_item_criado");
        }
    }
}
