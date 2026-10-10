namespace EasyStock.Domain.Entities;

/// <summary>Intenção persistida antes do envio ao provedor; só confirmação gera saída no Caixa.</summary>
public sealed class PedidoEstornoOnline
{
    public const string Pendente = "pendente";
    public const string Confirmado = "confirmado";
    public const string Recusado = "recusado";
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid PedidoId { get; set; }
    public Guid PagamentoId { get; set; }
    public string PagamentoExternoId { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public Guid? UsuarioId { get; set; }
    public string? UsuarioNome { get; set; }
    public string Situacao { get; set; } = Pendente;
    public string? EstornoExternoId { get; set; }
    public string? Detalhe { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? EnviadoEm { get; set; }
    public DateTime? ConfirmadoEm { get; set; }
    public Guid? MovimentoCaixaId { get; set; }
}
