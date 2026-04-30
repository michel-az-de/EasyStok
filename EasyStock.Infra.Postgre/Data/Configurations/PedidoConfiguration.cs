using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class PedidoConfiguration : IEntityTypeConfiguration<Pedido>
    {
        public void Configure(EntityTypeBuilder<Pedido> b)
        {
            b.ToTable("pedidos");
            b.HasKey(p => p.Id);

            b.Property(p => p.ClienteNome).HasMaxLength(150);
            b.Property(p => p.ClienteApt).HasMaxLength(32);
            b.Property(p => p.ClienteTelefone).HasMaxLength(32);
            b.Property(p => p.Status).IsRequired().HasMaxLength(20);
            b.Property(p => p.Total).HasColumnType("numeric(14,2)");
            b.Property(p => p.Observacoes).HasColumnType("text");
            b.Property(p => p.Origem).HasMaxLength(20);
            b.Property(p => p.MobileOrderId).HasMaxLength(64);

            b.HasOne(p => p.Empresa)
                .WithMany()
                .HasForeignKey(p => p.EmpresaId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(p => p.Cliente)
                .WithMany()
                .HasForeignKey(p => p.ClienteId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(p => p.Loja)
                .WithMany()
                .HasForeignKey(p => p.LojaId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(p => p.Venda)
                .WithMany()
                .HasForeignKey(p => p.VendaId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasMany(p => p.Itens)
                .WithOne(i => i.Pedido)
                .HasForeignKey(i => i.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(p => p.Eventos)
                .WithOne(e => e.Pedido)
                .HasForeignKey(e => e.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(p => p.Pagamentos)
                .WithOne(pg => pg.Pedido)
                .HasForeignKey(pg => pg.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class PedidoItemConfiguration : IEntityTypeConfiguration<PedidoItem>
    {
        public void Configure(EntityTypeBuilder<PedidoItem> b)
        {
            b.ToTable("pedido_itens");
            b.HasKey(x => x.Id);
            b.Property(x => x.Nome).IsRequired().HasMaxLength(150);
            b.Property(x => x.Emoji).HasMaxLength(16);
            b.Property(x => x.Unidade).HasMaxLength(32);
            b.Property(x => x.Quantidade).HasColumnType("numeric(14,3)");
            b.Property(x => x.PrecoUnitario).HasColumnType("numeric(14,2)");
            b.Property(x => x.Subtotal).HasColumnType("numeric(14,2)");
            b.Property(x => x.Observacao).HasColumnType("text");

            b.HasOne(x => x.Produto)
                .WithMany()
                .HasForeignKey(x => x.ProdutoId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }

    public class PedidoEventoConfiguration : IEntityTypeConfiguration<PedidoEvento>
    {
        public void Configure(EntityTypeBuilder<PedidoEvento> b)
        {
            b.ToTable("pedido_eventos");
            b.HasKey(x => x.Id);
            b.Property(x => x.Tipo).IsRequired().HasMaxLength(40);
            b.Property(x => x.StatusAntigo).HasMaxLength(20);
            b.Property(x => x.StatusNovo).HasMaxLength(20);
            b.Property(x => x.Detalhes).HasColumnType("text");
            b.Property(x => x.UsuarioNome).HasMaxLength(120);
            b.Property(x => x.Origem).HasMaxLength(20);
        }
    }

    public class PedidoPagamentoConfiguration : IEntityTypeConfiguration<PedidoPagamento>
    {
        public void Configure(EntityTypeBuilder<PedidoPagamento> b)
        {
            b.ToTable("pedido_pagamentos");
            b.HasKey(x => x.Id);
            b.Property(x => x.Metodo).IsRequired().HasMaxLength(20);
            b.Property(x => x.Valor).HasColumnType("numeric(14,2)");
            b.Property(x => x.Referencia).HasMaxLength(120);
            b.Property(x => x.Observacao).HasColumnType("text");
            b.Property(x => x.RegistradoPorNome).HasMaxLength(120);
        }
    }
}
