namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public sealed class FaqFeedbackConfiguration : IEntityTypeConfiguration<FaqFeedback>
    {
        public void Configure(EntityTypeBuilder<FaqFeedback> builder)
        {
            builder.ToTable("faq_feedbacks");
            builder.HasKey(f => f.Id);

            builder.Property(f => f.IpHash).IsRequired().HasMaxLength(64);
            builder.Property(f => f.Comentario).HasMaxLength(1000);

            builder.HasOne(f => f.Item)
                .WithMany()
                .HasForeignKey(f => f.ItemId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(f => new { f.ItemId, f.Util })
                .HasDatabaseName("ix_faq_feedbacks_item_util");
        }
    }
}
