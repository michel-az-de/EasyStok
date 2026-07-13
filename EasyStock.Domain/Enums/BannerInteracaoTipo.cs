namespace EasyStock.Domain.Enums
{
    /// <summary>
    /// Tipo de interação de um usuário com um banner. <see cref="Visto"/> = dispensou
    /// (ou visualização única auto-registrada); <see cref="Confirmado"/> = clicou
    /// "Ok, recebi" (acknowledgement auditável). Confirmado é estritamente mais forte
    /// que Visto na consulta de banners ativos. <see cref="Impressao"/> = exibição
    /// registrada (alcance) — puramente analítico: NUNCA esconde o banner da fila de
    /// ativos (senão a faixa sumiria já na primeira exibição). Persistido como string
    /// (varchar(20)); valores só podem crescer no fim.
    /// </summary>
    public enum BannerInteracaoTipo
    {
        Visto = 0,
        Confirmado = 1,
        Impressao = 2
    }
}
