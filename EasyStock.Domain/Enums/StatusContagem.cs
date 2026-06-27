namespace EasyStock.Domain.Enums
{
    /// <summary>
    /// Estados de uma sessao de Contagem (ADR Inventario). Transicoes (em Contagem):
    /// Rascunho -> EmAndamento -> (Pausada &lt;-&gt; EmAndamento) -> Finalizada -> Aplicada.
    /// Cancelada a partir de qualquer estado nao-terminal. Aplicada/Cancelada sao terminais.
    /// </summary>
    public enum StatusContagem
    {
        Rascunho,
        EmAndamento,
        Pausada,
        Finalizada,
        Aplicada,
        Cancelada
    }
}
