namespace EasyStock.Domain.Enums
{
    /// <summary>
    /// Tipo de interação de um usuário com um banner. <see cref="Visto"/> = dispensou
    /// (ou visualização única auto-registrada); <see cref="Confirmado"/> = clicou
    /// "Ok, recebi" (acknowledgement auditável). Confirmado é estritamente mais forte
    /// que Visto na consulta de banners ativos.
    /// </summary>
    public enum BannerInteracaoTipo
    {
        Visto = 0,
        Confirmado = 1
    }
}
