namespace EasyStock.Domain.Entities.Operacao;

/// <summary>
/// Contador do número do dia do pedido (S53): o último número dado pela empresa num dia de produção. Só o
/// banco avança este contador (upsert atômico em <c>ISequenciaDiariaPedido</c>); a aplicação nunca instancia.
/// </summary>
public class SequenciaDiariaPedido
{
    public Guid EmpresaId { get; private set; }

    /// <summary>Dia de produção (data civil de Brasília).</summary>
    public DateOnly Data { get; private set; }

    /// <summary>Último número entregue no dia.</summary>
    public int Ultimo { get; private set; }

    private SequenciaDiariaPedido() { }
}
