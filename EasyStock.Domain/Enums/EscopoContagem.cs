namespace EasyStock.Domain.Enums
{
    /// <summary>
    /// Abrangencia da contagem. Loja filtra por ItemEstoque.LojaId DENTRO do tenant
    /// (Loja e subdivisao da Empresa, nao um tenant). Categoria filtra pelos produtos
    /// da categoria. O conjunto e congelado no start (membership) para ser estavel.
    /// </summary>
    public enum EscopoContagem
    {
        Todos,
        Categoria,
        Loja
    }
}
