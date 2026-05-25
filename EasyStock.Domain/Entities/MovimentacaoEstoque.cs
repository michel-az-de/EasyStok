using System;
using EasyStock.Domain.ValueObjects;
using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Entities
{
    public class MovimentacaoEstoque
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid ItemEstoqueId { get; set; }
        public Guid ProdutoId { get; set; }
        public Guid? ProdutoVariacaoId { get; set; }
        public Guid? VendaId { get; set; }

        public TipoMovimentacaoEstoque Tipo { get; set; }
        public NaturezaMovimentacaoEstoque Natureza { get; set; }
        public Quantidade Quantidade { get; set; } = null!;

        public Dinheiro? ValorUnitario { get; set; }
        public Dinheiro? ValorTotal { get; set; }

        public DateTime DataMovimentacao { get; set; }
        public string? Descricao { get; set; }
        public string? DocumentoReferencia { get; set; }
        public DateTime CriadoEm { get; set; }

        public Guid? MovimentacaoEstornadaId { get; set; }
        public DateTime? EstornadaEm { get; set; }

        // Auditoria de quem/de onde/em que dispositivo a movimentacao foi originada.
        // Nullable porque registros legados nao possuem; preenchido em todas
        // as movimentacoes novas via ICurrentUserAccessor.
        public Guid? UsuarioId { get; set; }
        public string? Ip { get; set; }
        public string? UserAgent { get; set; }
        public string? DispositivoId { get; set; }

        // Motivo do estorno (preenchido apenas em movimentacoes do tipo Estorno).
        public string? MotivoEstorno { get; set; }

        public Empresa? Empresa { get; set; }
        public ItemEstoque? ItemEstoque { get; set; }
        public Produto? Produto { get; set; }
        public ProdutoVariacao? ProdutoVariacao { get; set; }
        public Venda? Venda { get; set; }
        public MovimentacaoEstoque? MovimentacaoEstornada { get; set; }

        public static MovimentacaoEstoque CriarEntrada(
            Guid id,
            Guid empresaId,
            ItemEstoque item,
            NaturezaMovimentacaoEstoque natureza,
            Quantidade quantidade,
            Dinheiro valorUnitario,
            DateTime dataMovimentacao,
            string? descricao,
            string? documentoReferencia,
            DateTime criadoEm,
            AuditoriaContexto? auditoria = null) =>
            new()
            {
                Id = id,
                EmpresaId = empresaId,
                ItemEstoqueId = item.Id,
                ProdutoId = item.ProdutoId,
                ProdutoVariacaoId = item.ProdutoVariacaoId,
                Tipo = TipoMovimentacaoEstoque.Entrada,
                Natureza = natureza,
                Quantidade = quantidade,
                ValorUnitario = valorUnitario,
                ValorTotal = Dinheiro.FromDecimal(valorUnitario.Valor * quantidade.Value),
                DataMovimentacao = dataMovimentacao,
                Descricao = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim(),
                DocumentoReferencia = string.IsNullOrWhiteSpace(documentoReferencia) ? null : documentoReferencia.Trim(),
                CriadoEm = criadoEm,
                UsuarioId = auditoria?.UsuarioId,
                Ip = auditoria?.Ip,
                UserAgent = auditoria?.UserAgent,
                DispositivoId = auditoria?.DispositivoId
            };

        public static MovimentacaoEstoque CriarSaida(
            Guid id,
            Guid empresaId,
            ItemEstoque item,
            Guid vendaId,
            NaturezaMovimentacaoEstoque natureza,
            Quantidade quantidade,
            Dinheiro valorUnitario,
            DateTime dataMovimentacao,
            string? descricao,
            string? documentoReferencia,
            DateTime criadoEm,
            AuditoriaContexto? auditoria = null) =>
            new()
            {
                Id = id,
                EmpresaId = empresaId,
                ItemEstoqueId = item.Id,
                ProdutoId = item.ProdutoId,
                ProdutoVariacaoId = item.ProdutoVariacaoId,
                VendaId = vendaId,
                Tipo = TipoMovimentacaoEstoque.Saida,
                Natureza = natureza,
                Quantidade = quantidade,
                ValorUnitario = valorUnitario,
                ValorTotal = Dinheiro.FromDecimal(valorUnitario.Valor * quantidade.Value),
                DataMovimentacao = dataMovimentacao,
                Descricao = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim(),
                DocumentoReferencia = string.IsNullOrWhiteSpace(documentoReferencia) ? null : documentoReferencia.Trim(),
                CriadoEm = criadoEm,
                UsuarioId = auditoria?.UsuarioId,
                Ip = auditoria?.Ip,
                UserAgent = auditoria?.UserAgent,
                DispositivoId = auditoria?.DispositivoId
            };

        public void MarcarComoEstornada(DateTime estornadaEm)
        {
            if (EstornadaEm.HasValue)
                throw new Exceptions.MovimentacaoJaEstornadaException(Id);

            EstornadaEm = estornadaEm;
        }

        public static MovimentacaoEstoque CriarEstorno(
            Guid id,
            MovimentacaoEstoque original,
            DateTime dataEstorno,
            string? descricao,
            DateTime criadoEm,
            string? motivoEstorno = null,
            AuditoriaContexto? auditoria = null) =>
            new()
            {
                Id = id,
                EmpresaId = original.EmpresaId,
                ItemEstoqueId = original.ItemEstoqueId,
                ProdutoId = original.ProdutoId,
                ProdutoVariacaoId = original.ProdutoVariacaoId,
                VendaId = null,
                Tipo = TipoMovimentacaoEstoque.Entrada,
                Natureza = NaturezaMovimentacaoEstoque.Estorno,
                Quantidade = original.Quantidade,
                ValorUnitario = original.ValorUnitario,
                ValorTotal = original.ValorTotal,
                DataMovimentacao = dataEstorno,
                Descricao = string.IsNullOrWhiteSpace(descricao) ? $"Estorno da movimentacao {original.Id}" : descricao.Trim(),
                DocumentoReferencia = original.DocumentoReferencia,
                MovimentacaoEstornadaId = original.Id,
                CriadoEm = criadoEm,
                MotivoEstorno = string.IsNullOrWhiteSpace(motivoEstorno) ? null : motivoEstorno.Trim(),
                UsuarioId = auditoria?.UsuarioId,
                Ip = auditoria?.Ip,
                UserAgent = auditoria?.UserAgent,
                DispositivoId = auditoria?.DispositivoId
            };
    }

    /// <summary>
    /// Contexto de auditoria capturado no momento da movimentacao
    /// (quem/de onde/qual dispositivo). Imutavel, opcional para
    /// retrocompatibilidade com testes que nao injetam ICurrentUserAccessor.
    /// </summary>
    public sealed record AuditoriaContexto(
        Guid? UsuarioId,
        string? Ip,
        string? UserAgent,
        string? DispositivoId);
}
