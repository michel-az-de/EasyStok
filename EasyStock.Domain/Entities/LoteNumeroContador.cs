namespace EasyStock.Domain.Entities;

/// <summary>
/// Contador do número curto do lote (#2199, D4): o último número dado na loja. Só o banco avança este contador
/// (função <c>lote_numero_curto_proximo</c>, chamada pelo trigger de <c>lotes</c>); a aplicação nunca instancia.
/// </summary>
public class LoteNumeroContador
{
    public Guid EmpresaId { get; private set; }

    /// <summary>Loja do lote, ou <see cref="Guid.Empty"/> para lote sem loja.</summary>
    public Guid LojaId { get; private set; }

    public long Ultimo { get; private set; }

    private LoteNumeroContador() { }
}
