namespace EasyStock.Domain.Enums
{
    /// <summary>
    /// Distingue produtos com rotulagem obrigatoria (RDC 727/2022 - conteudo
    /// liquido em embalagem fechada) dos avulsos/granel.
    ///
    /// Avulso (default): pao por kg, granel, fracionado na hora.
    ///   Etiqueta sem peso obrigatorio.
    /// Embalado: massa fresca em pacote, congelado em vacuo, produto
    ///   pre-embalado. Peso por unidade obrigatorio na etiqueta.
    ///
    /// Inserido em 2026-05-16 para correcao C2 (LOT-260516 sem peso).
    /// </summary>
    public enum TipoEmbalagem
    {
        Avulso = 0,
        Embalado = 1
    }
}
