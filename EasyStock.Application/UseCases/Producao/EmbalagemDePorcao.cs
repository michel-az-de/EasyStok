namespace EasyStock.Application.UseCases.Producao;

/// <summary>
/// M1.4e (#1552): o que serve de embalagem de uma porção. A mesma regra vale ao escolher, no cardápio,
/// e ao produzir: produto ativo, marcado como embalagem e contado por unidade (desce uma por porção).
/// </summary>
public static class EmbalagemDePorcao
{
    /// <summary>Por que o produto não serve, ou null se serve. "Tirar do estoque" só inativa o produto.</summary>
    public static string? MotivoParaNaoUsar(Produto? embalagem) => embalagem switch
    {
        null => "a embalagem escolhida para a porção não foi encontrada",
        { Status: not StatusProduto.Ativo } => $"{embalagem.Nome} está fora do estoque (inativa)",
        { EhEmbalagem: false } => $"{embalagem.Nome} não está mais marcada como embalagem",
        { UnidadeMedidaBase: not UnidadeMedida.Un } => $"{embalagem.Nome} não é contada por unidade",
        _ => null,
    };
}
