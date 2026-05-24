namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando o telefone excedeu a cota de OTPs/hora (anti-abuso).
///
/// <para>
/// <see cref="RetryAfterSeconds"/> é exposto no header HTTP <c>Retry-After</c>
/// e no JSON de resposta (<c>{"retryAfterSeconds": N}</c>).
/// </para>
/// </summary>
public class OtpRateLimitExcedidoException : RegraDeDominioVioladaException
{
    public int RetryAfterSeconds { get; }

    public OtpRateLimitExcedidoException(int retryAfterSeconds)
        : base($"Muitas tentativas de OTP. Aguarde {retryAfterSeconds} segundos.")
    {
        RetryAfterSeconds = retryAfterSeconds;
    }

    public OtpRateLimitExcedidoException(int retryAfterSeconds, string message)
        : base(message)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}
