namespace EasyStock.Application.UseCases.Storefront.Auth;

/// <summary>
/// Saída do <see cref="ValidarOtpUseCase"/>.
///
/// <para>
/// <see cref="SessionId"/>: ID da <c>ClienteSession</c> criada — valor do cookie
/// <c>__Host-cdb_session</c>. O controller converte para string e aplica o cookie.
/// </para>
/// </summary>
public sealed record ValidarOtpResult(
    Guid SessionId,
    string TelefoneOfuscado,
    string PrimeiroNome,
    int MaxAgeSecs);
