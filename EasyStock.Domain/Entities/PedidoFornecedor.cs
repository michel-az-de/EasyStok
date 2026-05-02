using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Entities;

public class PedidoFornecedor
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid FornecedorId { get; set; }
    public Guid? LojaId { get; set; }
    public DateTime DataPedido { get; set; }
    public DateTime? PrevisaoEntrega { get; set; }
    public DateTime? DataRecebimento { get; set; }
    public decimal? ValorEstimado { get; set; }
    public StatusPedidoFornecedor Status { get; set; }
    public string? Canal { get; set; }
    public string? Tracking { get; set; }
    public string? Observacoes { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    public Fornecedor? Fornecedor { get; set; }

    public ICollection<PedidoFornecedorItem> Itens { get; set; } = new List<PedidoFornecedorItem>();
}
