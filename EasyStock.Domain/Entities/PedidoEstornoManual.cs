namespace EasyStock.Domain.Entities;

// Confirmação de uma devolução já realizada fora do provedor. O recebimento original permanece.
public sealed class PedidoEstornoManual
{
    public const string OrigemCaixa = "devolucao_pedido";
    public Guid Id { get; init; }
    public Guid EmpresaId { get; init; }
    public Guid PedidoId { get; init; }
    public Guid PagamentoId { get; init; }
    public decimal Valor { get; init; }
    public string Metodo { get; init; } = "";
    public string Motivo { get; init; } = "";
    public string Referencia { get; init; } = "";
    public Guid UsuarioId { get; init; }
    public string? UsuarioNome { get; init; }
    public DateTime RegistradoEm { get; init; }
}
