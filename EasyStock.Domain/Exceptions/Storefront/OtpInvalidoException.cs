namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando o código OTP não é encontrado ou o código informado não bate.
/// Mensagem genérica intencional — não confirma se o telefone existe (anti-enumeração).
/// Mapeada para HTTP 401.
/// </summary>
public class OtpInvalidoException : RegraDeDominioVioladaException
{
    public OtpInvalidoException()
        : base("Código inválido ou expirado.")
    {
    }
}
