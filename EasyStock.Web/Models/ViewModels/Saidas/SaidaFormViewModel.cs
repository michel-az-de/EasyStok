using System.ComponentModel.DataAnnotations;
using EasyStock.Web.Helpers;
using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Models.ViewModels.Saidas;

public class SaidaFormViewModel
{
    public string? ProdutoId { get; set; }
    public string? VarId { get; set; }
    // Quando preenchido, força saída do lote específico (não FIFO/FEFO).
    // Setado pelo fluxo "saída rápida" a partir da listagem de estoque.
    public string? ItemEstoqueId { get; set; }

    [Required(ErrorMessage = "Natureza é obrigatória")]
    public string Natureza { get; set; } = "venda";

    [Required(ErrorMessage = "Quantidade é obrigatória")]
    [Range(1, int.MaxValue, ErrorMessage = "Quantidade deve ser maior que zero")]
    public int Qty { get; set; }

    // Valor unitário é opcional (null = sem valor informado), mas quando informado
    // não pode ser negativo. A regra "Venda exige > 0" vive no use case da API
    // (RegistrarSaidaEstoque); aqui barramos o negativo em qualquer natureza.
    [Range(0, double.MaxValue, ErrorMessage = "Valor unitário não pode ser negativo.")]
    public decimal? Valor { get; set; }

    // BrazilTime.Today em vez de DateTime.Today — server roda UTC; na janela
    // 21h–23:59 BRT, DateTime.Today retornava o dia seguinte e a tela pre-fillava
    // a data errada.
    [Required(ErrorMessage = "Data é obrigatória")]
    public DateOnly DtVenda { get; set; } = BrazilTime.Today();

    public DateOnly? DtSaida { get; set; }
    public DateOnly? DtEnvio { get; set; }
    public string? NotaFiscal { get; set; }
    public string? Canal { get; set; }
    public string? Descricao { get; set; }

    // Preenchidos pela view
    public Produto? ProdutoSelecionado { get; set; }
    public int QtyDisponivel { get; set; }
}
