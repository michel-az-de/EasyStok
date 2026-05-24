namespace EasyStock.Application.UseCases.Storefront.Auth;

/// <summary>
/// Entrada do <see cref="SolicitarOtpUseCase"/>.
///
/// <list type="bullet">
///   <item><see cref="Slug"/>: identificador público do storefront (resolve EmpresaId).</item>
///   <item><see cref="Telefone"/>: telefone bruto digitado pelo cliente — será normalizado para E.164 BR.</item>
///   <item><see cref="IdempotencyKey"/>: header opcional <c>X-Idempotency-Key</c>. Usado para correlation/log; idempotência efetiva vem da janela de 60s no use case.</item>
///   <item><see cref="IpOrigem"/>: IP do cliente (logging + auditoria).</item>
///   <item><see cref="UserAgent"/>: UA do navegador (logging + auditoria).</item>
/// </list>
/// </summary>
public sealed record SolicitarOtpInput(
    string Slug,
    string Telefone,
    string? IdempotencyKey,
    string? IpOrigem,
    string? UserAgent);
