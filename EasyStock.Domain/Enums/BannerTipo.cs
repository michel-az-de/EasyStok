namespace EasyStock.Domain.Enums
{
    /// <summary>
    /// Forma do banner de plataforma. <see cref="Imagem"/> exibe uma imagem
    /// (opcionalmente clicável, com tooltip e tamanho); <see cref="Mensagem"/>
    /// não tem imagem e é apresentado como modal com título + corpo.
    /// </summary>
    public enum BannerTipo
    {
        Imagem = 0,
        Mensagem = 1
    }
}
