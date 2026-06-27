namespace EasyStock.Domain.Enums
{
    /// <summary>
    /// Como os lotes entram na contagem. Guiado (MVP): o sistema lista os lotes
    /// esperados e o operador confirma/ajusta. Descoberta (V2): o operador le a
    /// validade impressa e lanca por validade encontrada; o sistema mapeia depois.
    /// </summary>
    public enum EstrategiaLoteContagem
    {
        Guiado,
        Descoberta
    }
}
