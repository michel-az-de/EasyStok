using EasyStock.Domain.Exceptions;

namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Status do processamento de um webhook recebido. Ver
/// <see cref="WebhookProcessado"/> e ADR-0006.
/// </summary>
public enum WebhookProcessadoStatus
{
    /// <summary>Persistido pelo endpoint, ainda não passou pelo handler.</summary>
    Received = 0,

    /// <summary>Handler processou com sucesso e resolveu <c>EmpresaId</c>.</summary>
    Processed = 1,

    /// <summary>Handler rodou mas não bateu com nenhuma Fatura (ex: external_reference desconhecida).</summary>
    Orphan = 2,

    /// <summary>Handler crashou — guardamos motivo (mensagem da exceção).</summary>
    Error = 3,
}

/// <summary>
/// Registro de cada webhook inbound de provider (MercadoPago hoje).
///
/// Implementa o padrão <strong>receive-then-process</strong> (ADR-0006):
/// o endpoint público persiste o payload cru com <see cref="WebhookProcessadoStatus.Received"/>
/// dentro de uma transação curta, e só depois um handler em background tenta
/// resolver para qual <c>Fatura</c>/<c>EmpresaId</c> ele pertence. Garante que
/// nunca perdemos webhook por falha downstream e dá rastro completo para debug.
///
/// <para>
/// <strong>Dedup</strong>: índice único <c>(Provider, EventoId)</c> em EF Config —
/// se o MercadoPago reenviar o mesmo <c>payment.id</c>, o segundo insert falha
/// com unique violation e o endpoint responde 200 (já recebido).
/// </para>
///
/// <para>
/// <strong>EmpresaId é nullable</strong> de propósito: só é resolvido em
/// <see cref="MarcarProcessado"/>. Webhooks <see cref="WebhookProcessadoStatus.Received"/>
/// e <see cref="WebhookProcessadoStatus.Orphan"/> não têm empresa conhecida.
/// </para>
/// </summary>
public class WebhookProcessado
{
    public Guid Id { get; private set; }
    public string Provider { get; private set; } = null!;
    public string EventoId { get; private set; } = null!;
    public string Tipo { get; private set; } = null!;
    public string PayloadRaw { get; private set; } = null!;

    public WebhookProcessadoStatus Status { get; private set; }
    public string? Motivo { get; private set; }

    public DateTime RecebidoEm { get; private set; }
    public DateTime? ProcessadoEm { get; private set; }

    /// <summary>
    /// Empresa resolvida pelo handler ao processar payload. Null até
    /// <see cref="MarcarProcessado"/> ser chamado. Permanece null para órfãos
    /// (não bateu nenhuma Fatura).
    /// </summary>
    public Guid? EmpresaId { get; private set; }

    // EF Core ctor sem parâmetros
    private WebhookProcessado() { }

    /// <summary>
    /// Factory. Cria registro em <see cref="WebhookProcessadoStatus.Received"/>;
    /// processamento é responsabilidade do handler downstream.
    /// </summary>
    public static WebhookProcessado Receber(
        string provider,
        string eventoId,
        string tipo,
        string payloadRaw)
    {
        var providerNormalizado = NormalizarProvider(provider);
        ValidarProvider(providerNormalizado);
        ValidarEventoId(eventoId);
        ValidarTipo(tipo);
        ValidarPayload(payloadRaw);

        return new WebhookProcessado
        {
            Id = Guid.NewGuid(),
            Provider = providerNormalizado,
            EventoId = eventoId.Trim(),
            Tipo = tipo.Trim(),
            PayloadRaw = payloadRaw,
            Status = WebhookProcessadoStatus.Received,
            RecebidoEm = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Transiciona de <see cref="WebhookProcessadoStatus.Received"/> para
    /// <see cref="WebhookProcessadoStatus.Processed"/>, registrando empresa
    /// resolvida e timestamp.
    /// </summary>
    public void MarcarProcessado(Guid empresaId)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId é obrigatório para marcar processado.");

        if (Status != WebhookProcessadoStatus.Received)
            throw new RegraDeDominioVioladaException(
                $"Não é possível marcar processado a partir do status '{Status}' (esperado: Received).");

        Status = WebhookProcessadoStatus.Processed;
        EmpresaId = empresaId;
        ProcessadoEm = DateTime.UtcNow;
        Motivo = null;
    }

    /// <summary>
    /// Marca como órfão (handler rodou mas não conseguiu resolver Fatura/Empresa).
    /// Permitido apenas a partir de <see cref="WebhookProcessadoStatus.Received"/>.
    /// </summary>
    public void MarcarOrfao(string motivo)
    {
        ValidarMotivo(motivo);

        if (Status != WebhookProcessadoStatus.Received)
            throw new RegraDeDominioVioladaException(
                $"Não é possível marcar órfão a partir do status '{Status}' (esperado: Received).");

        Status = WebhookProcessadoStatus.Orphan;
        Motivo = motivo.Trim();
        ProcessadoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Marca como erro. Permitido a partir de <see cref="WebhookProcessadoStatus.Received"/>
    /// ou <see cref="WebhookProcessadoStatus.Orphan"/> (orphan→error cobre retry que crashou).
    /// </summary>
    public void MarcarErro(string motivo)
    {
        ValidarMotivo(motivo);

        if (Status != WebhookProcessadoStatus.Received && Status != WebhookProcessadoStatus.Orphan)
            throw new RegraDeDominioVioladaException(
                $"Não é possível marcar erro a partir do status '{Status}' (esperado: Received ou Orphan).");

        Status = WebhookProcessadoStatus.Error;
        Motivo = motivo.Trim();
        ProcessadoEm = DateTime.UtcNow;
    }

    // ── Validações privadas ────────────────────────────────────────────

    private static string NormalizarProvider(string? provider) =>
        (provider ?? string.Empty).Trim().ToLowerInvariant();

    private static void ValidarProvider(string providerNormalizado)
    {
        if (string.IsNullOrWhiteSpace(providerNormalizado))
            throw new RegraDeDominioVioladaException("Provider é obrigatório.");
        if (providerNormalizado.Length > 32)
            throw new RegraDeDominioVioladaException(
                $"Provider não pode exceder 32 caracteres (recebido: {providerNormalizado.Length}).");
    }

    private static void ValidarEventoId(string eventoId)
    {
        if (string.IsNullOrWhiteSpace(eventoId))
            throw new RegraDeDominioVioladaException("EventoId é obrigatório.");
        if (eventoId.Trim().Length > 128)
            throw new RegraDeDominioVioladaException(
                $"EventoId não pode exceder 128 caracteres (recebido: {eventoId.Trim().Length}).");
    }

    private static void ValidarTipo(string tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo))
            throw new RegraDeDominioVioladaException("Tipo é obrigatório.");
        if (tipo.Trim().Length > 64)
            throw new RegraDeDominioVioladaException(
                $"Tipo não pode exceder 64 caracteres (recebido: {tipo.Trim().Length}).");
    }

    private static void ValidarPayload(string payload)
    {
        if (string.IsNullOrEmpty(payload))
            throw new RegraDeDominioVioladaException("PayloadRaw é obrigatório.");
    }

    private static void ValidarMotivo(string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new RegraDeDominioVioladaException("Motivo é obrigatório.");
    }
}
