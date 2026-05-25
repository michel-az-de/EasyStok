namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando o código OTP já expirou (> 5 minutos após emissão).
/// Mapeada para HTTP 410 Gone.
/// </summary>
public class OtpExpiradoException : RegraDeDominioVioladaException
{
    public OtpExpiradoException()
        : base("Código expirou. Solicite um novo código.")
    {
    }
}
