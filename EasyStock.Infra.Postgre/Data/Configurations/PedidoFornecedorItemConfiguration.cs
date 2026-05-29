namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class PedidoFornecedorItemConfiguration : IEntityTypeConfiguration<PedidoFornecedorItem>
    {
        public void Configure(EntityTypeBuilder<PedidoFornecedorItem> builder)
        {
            builder.ToTable("pedidos_fornecedor_itens");
            builder.HasKey(x => x.Id);

            // Propriedades
            builder.Property(x => x.Nome)
                .HasMaxLength(500)
                .IsRequired();

            builder.Property(x => x.Unidade)
                .HasMaxLength(50);

            builder.Property(x => x.Quantidade)
                .HasColumnType("decimal(18,4)")
                .IsRequired();

            builder.Property(x => x.QuantidadeRecebida)
                .HasColumnType("decimal(18,4)")
                .HasDefaultValue(0);

            builder.Property(x => x.CustoUnitario)
                .HasColumnType("decimal(18,6)")
                .IsRequired();

            builder.Property(x => x.Observacao)
                .HasColumnType("text");

            // Relacionamentos
            builder.HasOne(x => x.PedidoFornecedor)
                .WithMany(p => p.Itens)
                .HasForeignKey(x => x.PedidoFornecedorId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Produto)
                .WithMany()
                .HasForeignKey(x => x.ProdutoId)
                .OnDelete(DeleteBehavior.SetNull);

            // Índices
            builder.HasIndex(x => x.PedidoFornecedorId);
            builder.HasIndex(x => x.ProdutoId);
        }
    }
}
