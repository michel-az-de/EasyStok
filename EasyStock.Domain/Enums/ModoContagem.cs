namespace EasyStock.Domain.Enums
{
    /// <summary>
    /// Visibilidade da quantidade esperada durante a contagem. Cego esconde a QTD
    /// esperada (evita vies), mas SEMPRE mostra quais lotes existem (as validades) —
    /// senao e impossivel contar por lote.
    /// </summary>
    public enum ModoContagem
    {
        Visivel,
        Cego
    }
}
