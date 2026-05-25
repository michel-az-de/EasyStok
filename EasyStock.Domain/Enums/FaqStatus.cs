namespace EasyStock.Domain.Enums
{
    /// <summary>
    /// Ciclo de vida de um artigo de FAQ: rascunho → publicado → arquivado.
    /// Apenas artigos <see cref="Publicado"/> são visíveis ao usuário final.
    /// </summary>
    public enum FaqStatus
    {
        Rascunho = 0,
        Publicado = 1,
        Arquivado = 2
    }
}
