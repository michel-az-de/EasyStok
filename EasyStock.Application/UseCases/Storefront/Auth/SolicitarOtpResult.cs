namespace EasyStock.Application.UseCases.Storefront.Auth;

/// <summary>
/// Saída do <see cref="SolicitarOtpUseCase"/>.
///
/// <para>
/// <see cref="ExpiresInSeconds"/>: TTL do código emitido. Vem de
/// <c>ClienteOtp.TempoVidaPadrao</c> (5 minutos = 300s).
/// </para>
///
/// <para>
/// <see cref="Reaproveitado"/>: <c>true</c> quando idempotência kicked in
/// (OTP existente nos últimos 60s foi retornado em vez de gerar/enviar novo).
/// Útil para o controller decidir métrica/log diferenciado — não é exposto
/// na response pública.
/// </para>
/// </summary>
public sealed record SolicitarOtpResult(
    int ExpiresInSeconds,
    bool Reaproveitado);
