namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Falha ao enviar OTP pelo provider externo (WhatsApp/SMS). Wrapping da
/// exceção upstream — caller decide retry/fallback. Mapeada para HTTP 502
/// pelo global handler.
/// </summary>
public class OtpProviderException : Exception
{
    public OtpProviderException(string message)
        : base(message)
    {
    }

    public OtpProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
