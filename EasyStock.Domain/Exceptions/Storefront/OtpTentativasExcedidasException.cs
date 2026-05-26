namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando o OTP atingiu o limite de 5 tentativas incorretas.
/// O OTP é invalidado — cliente deve solicitar novo via AUTH-001.
/// Mapeada para HTTP 429 Too Many Requests.
/// </summary>
public class OtpTentativasExcedidasException : RegraDeDominioVioladaException
{
    public OtpTentativasExcedidasException()
        : base("Número máximo de tentativas atingido. Solicite um novo código.")
    {
    }
}
