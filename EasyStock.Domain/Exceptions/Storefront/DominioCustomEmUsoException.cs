namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Domínio próprio já apontado para outra vitrine. O índice único de <c>DominioCustom</c> barraria no
/// banco com erro genérico; aqui a regra aparece antes, como conflito (P01-B, #1173).
/// </summary>
public class DominioCustomEmUsoException : RegraDeDominioVioladaException
{
    public string Dominio { get; }

    public DominioCustomEmUsoException(string dominio)
        : base($"O domínio '{dominio}' já está em uso por outra vitrine.")
    {
        Dominio = dominio;
    }
}
