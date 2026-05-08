using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.ValueObjects;
using EasyStock.Domain.ValueObjects.Fiscal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Fiscal;

public sealed class NotaFiscalConfiguration : IEntityTypeConfiguration<NotaFiscal>
{
    public void Configure(EntityTypeBuilder<NotaFiscal> b)
    {
        b.ToTable("nota_fiscal");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.EmpresaId).HasColumnName("empresa_id").IsRequired();
        b.Property(x => x.LojaId).HasColumnName("loja_id");
        b.Property(x => x.PedidoId).HasColumnName("pedido_id");
        b.Property(x => x.VendaId).HasColumnName("venda_id");

        b.Property(x => x.Modelo)
            .HasColumnName("modelo")
            .HasConversion<short>()
            .IsRequired();

        b.Property(x => x.Serie).HasColumnName("serie").IsRequired();
        b.Property(x => x.Numero).HasColumnName("n_nf").IsRequired();

        b.Property(x => x.ChaveAcesso)
            .HasColumnName("chave_acesso")
            .HasMaxLength(44)
            .IsRequired()
            .HasConversion(
                vo => vo.Valor,
                str => ChaveAcessoNFe.Parse(str));

        b.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(40)
            .HasConversion<string>()
            .IsRequired();

        b.Property(x => x.TipoEmissao)
            .HasColumnName("tp_emis")
            .HasConversion<short>()
            .IsRequired();

        b.Property(x => x.Ambiente)
            .HasColumnName("tp_amb")
            .HasConversion<short>()
            .IsRequired();

        b.Property(x => x.DataEmissao).HasColumnName("dh_emi").IsRequired();
        b.Property(x => x.DataAutorizacao).HasColumnName("dh_autorizacao");
        b.Property(x => x.DataCancelamento).HasColumnName("dh_cancelamento");

        b.Property(x => x.ProtocoloAutorizacao).HasColumnName("protocolo_autorizacao").HasMaxLength(15);
        b.Property(x => x.ProtocoloCancelamento).HasColumnName("protocolo_cancelamento").HasMaxLength(15);

        b.Property(x => x.XmlAutorizado).HasColumnName("xml_autorizado").HasColumnType("text");
        b.Property(x => x.XmlAssinadoLocal).HasColumnName("xml_assinado_local").HasColumnType("text");
        b.Property(x => x.XmlEventoCancelamento).HasColumnName("xml_evento_cancelamento").HasColumnType("text");

        b.Property(x => x.MotivoRejeicao).HasColumnName("motivo_rejeicao").HasColumnType("text");
        b.Property(x => x.CodigoRejeicao).HasColumnName("cod_rejeicao").HasMaxLength(10);
        b.Property(x => x.JustificativaCancelamento)
            .HasColumnName("justificativa_cancelamento")
            .HasColumnType("text");

        b.Property(x => x.ClienteCpfCnpj).HasColumnName("cliente_cpf_cnpj").HasMaxLength(14);

        b.Property(x => x.ValorTotal)
            .HasColumnName("valor_total")
            .HasColumnType("numeric(14,2)")
            .HasConversion(d => d.Valor, dec => Dinheiro.FromDecimal(dec))
            .IsRequired();

        b.Property(x => x.FormaPagamentoPrincipal)
            .HasColumnName("forma_pagamento_principal")
            .HasMaxLength(2);

        b.Property(x => x.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(80)
            .IsRequired();

        b.Property(x => x.Origem).HasColumnName("origem").HasMaxLength(20);
        b.Property(x => x.CriadoPorUsuarioId).HasColumnName("created_by_user_id");
        b.Property(x => x.CriadoEm).HasColumnName("created_at").IsRequired();
        b.Property(x => x.AlteradoEm).HasColumnName("updated_at").IsRequired();
        b.Property(x => x.Arquivado).HasColumnName("arquivado").HasDefaultValue(false).IsRequired();

        // xmin como concurrency token (Postgres system column)
        b.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Coleções
        b.HasMany(x => x.Itens)
            .WithOne()
            .HasForeignKey("NotaFiscalId")
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Pagamentos)
            .WithOne()
            .HasForeignKey("NotaFiscalId")
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Eventos)
            .WithOne()
            .HasForeignKey("NotaFiscalId")
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        b.HasIndex(x => new { x.EmpresaId, x.LojaId, x.Modelo, x.Serie, x.Numero })
            .IsUnique()
            .HasDatabaseName("ux_nota_fiscal_empresa_loja_modelo_serie_nnf");

        b.HasIndex(x => new { x.EmpresaId, x.LojaId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_nota_fiscal_idempotency");

        b.HasIndex(x => x.ChaveAcesso)
            .HasDatabaseName("ix_nota_fiscal_chave");

        b.HasIndex(x => new { x.EmpresaId, x.Status, x.DataEmissao })
            .HasDatabaseName("ix_nota_fiscal_status_dh");
    }
}
