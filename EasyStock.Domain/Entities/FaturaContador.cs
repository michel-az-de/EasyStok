namespace EasyStock.Domain.Entities;

/// <summary>
/// Contador atomico de numeracao sequencial de <see cref="Fatura"/> por
/// empresa+ano. PK composta <c>(EmpresaId, Ano)</c>.
///
/// <para>
/// Acessado via <c>FaturaNumeradorService</c> com SQL puro:
/// </para>
/// <code>
/// INSERT INTO fatura_contador (empresa_id, ano, ultimo_numero, atualizado_em)
/// VALUES (@e, @y, 1, now())
/// ON CONFLICT (empresa_id, ano) DO UPDATE SET
///   ultimo_numero = fatura_contador.ultimo_numero + 1,
///   atualizado_em = now()
/// RETURNING ultimo_numero;
/// </code>
/// <para>
/// Ganhos: atomico em multi-pod, sem locking pessimista no agregado Fatura,
/// reset anual automatico (PK por ano), sem DDL por tenant.
/// </para>
/// </summary>
public class FaturaContador
{
    public Guid EmpresaId { get; set; }
    public int Ano { get; set; }
    public long UltimoNumero { get; set; }
    public DateTime AtualizadoEm { get; set; }
}
