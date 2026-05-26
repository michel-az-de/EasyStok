using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class PedidoConfiguration : IEntityTypeConfiguration<Pedido>
    {
        public void Configure(EntityTypeBuilder<Pedido> b)
        {
            b.ToTable("pedidos");
            b.HasKey(p => p.Id);

            // Optimistic concurrency via xmin (sem migration nova).
            b.Property<uint>("xmin")
                .HasColumnName("xmin")
                .IsRowVersion();

            b.Property(p => p.ClienteNome).HasMaxLength(150);
            b.Property(p => p.ClienteApt).HasMaxLength(32);
            b.Property(p => p.ClienteTelefone).HasMaxLength(32);
            // Status: 32 chars cobrem todos os enum values (maior atual = "aguardando_aprovacao_baba" = 25).
            // Aumento de 20 → 32 acompanha migration aditiva de TASK-EZ-APROVAR-001.
            b.Property(p => p.Status).IsRequired().HasMaxLength(32);

            // Total: Dinheiro VO persistido como decimal numeric(14,2).
            // Schema do DB inalterado — só o tipo no Domain virou tipado.
            // Materializa via Dinheiro.FromDecimal (lança em valor negativo,
            // sinalizando dado corrompido em vez de carregar silenciosamente).
            b.Property(p => p.Total)
                .HasConversion(
                    v => v.Valor,
                    v => Dinheiro.FromDecimal(v))
                .HasColumnType("numeric(14,2)");

            b.Property(p => p.Observacoes).HasColumnType("text");
            b.Property(p => p.Origem).HasMaxLength(20);
            b.Property(p => p.MobileOrderId).HasMaxLength(64);
            b.Property(p => p.AvaliacaoSolicitadaEm)
                .HasColumnName("avaliacao_solicitada_em")
                .IsRequired(false);

            // ── Resolução Storefront (TASK-EZ-APROVAR-001) ─────────────
            b.Property(p => p.AprovadoEm)
                .HasColumnName("aprovado_em")
                .IsRequired(false);
            b.Property(p => p.AprovadoPorUsuarioId)
                .HasColumnName("aprovado_por_usuario_id")
                .IsRequired(false);
            b.Property(p => p.RecusadoEm)
                .HasColumnName("recusado_em")
                .IsRequired(false);
            b.Property(p => p.RecusadoPorUsuarioId)
                .HasColumnName("recusado_por_usuario_id")
                .IsRequired(false);
            b.Property(p => p.MotivoRecusa)
                .HasColumnName("motivo_recusa")
                .HasMaxLength(40)
                .IsRequired(false);
            b.Property(p => p.MensagemRecusaCliente)
                .HasColumnName("mensagem_recusa_cliente")
                .HasMaxLength(280)
                .IsRequired(false);

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
