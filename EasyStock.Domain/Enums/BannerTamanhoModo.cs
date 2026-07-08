namespace EasyStock.Domain.Enums
{
    /// <summary>
    /// Como o tamanho da imagem do banner é definido: <see cref="HerdadoDaImagem"/>
    /// usa as dimensões naturais; <see cref="Manual"/> usa largura/altura em px.
    /// </summary>
    public enum BannerTamanhoModo
    {
        HerdadoDaImagem = 0,
        Manual = 1
    }
}
