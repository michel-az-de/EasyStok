namespace EasyStock.Application.UseCases.Storefront.Auth;

/// <summary>
/// Entrada do <see cref="ValidarOtpUseCase"/>.
///
/// <list type="bullet">
///   <item><see cref="Slug"/>: identificador público do storefront.</item>
///   <item><see cref="Telefone"/>: telefone bruto digitado pelo cliente — normalizado para E.164.</item>
///   <item><see cref="Codigo"/>: código OTP de 6 dígitos recebido via WhatsApp.</item>
///   <item><see cref="IpOrigem"/>: IP do cliente (logging + auditoria).</item>
///   <item><see cref="UserAgent"/>: UA do navegador — compõe o fingerprint de sessão.</item>
///   <item><see cref="AcceptLanguage"/>: header Accept-Language — compõe o fingerprint de sessão.</item>
/// </list>
/// </summary>
public sealed record ValidarOtpInput(
    string Slug,
    string Telefone,
    string Codigo,
    string? IpOrigem,
    string? UserAgent,
    string? AcceptLanguage);
