using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Domain.Entities.Pagamentos;

/// <summary>
/// Tentativa atomica de cobranca em UM gateway. Pode haver N attempts por
/// <see cref="FaturaPagamento"/> (fallback A→B, retries dentro do mesmo
/// gateway).
///
/// <para>
/// <b>Invariante critica</b>: dado um <c>FaturaPagamentoId</c>, no maximo UM
/// attempt pode ter <c>Status == Sucesso</c>. Garantido por partial unique
/// index no Postgres (<c>WHERE Status = 'Sucesso'</c>).
/// </para>
///
/// <para>
/// <b>Idempotencia</b>: <see cref="IdempotencyKey"/> derivada de
/// <c>SHA256(empresaId|faturaPagamentoId|provedor|tentativa)</c> e enviada ao
/// gateway via header HTTP (Stripe: <c>Idempotency-Key</c>; MercadoPago:
/// <c>X-Idempotency-Key</c>; Efí: derivada para <c>txid</c>).
/// </para>
///
/// <para>
/// <b>Audit</b>: cada transicao de status gera <see cref="PaymentAttemptEvent"/>.
/// </para>
///
/// <para>
/// <b>Multi-tenant</b>: <see cref="EmpresaId"/> NOT NULL — Global Query Filter
/// automatico aplica.
/// </para>
/// </summary>
public class PaymentAttempt
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid FaturaPagamentoId { get; set; }

    /// <summary>Denormalizado para query rapida sem JOIN com fatura_pagamentos.</summary>
    public Guid FaturaId { get; set; }

    /// <summary>Quando a origem do pagamento e uma <see cref="CobrancaAssinatura"/>.</summary>
    public Guid? CobrancaAssinaturaId { get; set; }

    /// <summary>"EfiPix" | "EfiBoleto" | "Stripe" | "MercadoPago" | "Manual" | etc.</summary>
    public string Provedor { get; set; } = null!;

    /// <summary>"pix" | "boleto" | "cartao" | "manual".</summary>
    public string Metodo { get; set; } = null!;

    public StatusPaymentAttempt Status { get; set; } = StatusPaymentAttempt.Iniciado;

    /// <summary>Sequencial 1..N por <see cref="FaturaPagamentoId"/>.</summary>
    public int Tentativa { get; set; }

    public DateTime IniciadoEm { get; set; }
    public DateTime? FinalizadoEm { get; set; }
    public int? LatenciaMs { get; set; }

    /// <summary>txid Pix, PaymentIntent ID Stripe, payment ID MercadoPago, etc.</summary>
    public string? GatewayTransactionId { get; set; }

    public ErrorCategory? ErrorCategory { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Reconciliador (P1) usa para agendar proxima consulta ao gateway.</summary>
    public DateTime? ProximaConsultaEm { get; set; }

    /// <summary>SHA-256 hex (64 chars). UNIQUE por <see cref="EmpresaId"/>.</summary>
    public string IdempotencyKey { get; set; } = null!;

    /// <summary>Header <c>Idempotency-Key</c> opcional recebido do cliente (idempotencia da intencao de pagamento).</summary>
    public string? ClientIdempotencyKey { get; set; }

    /// <summary>"global-priority" | "tenant-override" | "fallback-after-X" | "manual".</summary>
    public string RoutingMotivo { get; set; } = "global-priority";

    /// <summary>Resposta crua do gateway (jsonb), util para debug e dashboards.</summary>
    public string? MetadataJson { get; set; }

    /// <summary>RowVersion (xmin) — concorrencia otimista no Postgres.</summary>
    public uint Versao { get; set; }

    public FaturaPagamento? FaturaPagamento { get; set; }

    private PaymentAttempt() { }

    /// <summary>
    /// Cria attempt em estado <see cref="StatusPaymentAttempt.Iniciado"/>.
    /// Caller persiste em transacao com o <see cref="FaturaPagamento"/> antes
    /// de chamar o gateway — assim o ID e a IdempotencyKey ja existem caso a
    /// chamada falhe.
    /// </summary>
    public static PaymentAttempt Iniciar(
        Guid empresaId,
        Guid faturaPagamentoId,
        Guid faturaId,
        string provedor,
        string metodo,
        int tentativa,
        string idempotencyKey,
        string routingMotivo,
        Guid? cobrancaAssinaturaId = null,
        string? clientIdempotencyKey = null)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId e obrigatorio em PaymentAttempt.");
        if (faturaPagamentoId == Guid.Empty)
            throw new RegraDeDominioVioladaException("FaturaPagamentoId e obrigatorio em PaymentAttempt.");
        if (faturaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("FaturaId e obrigatorio em PaymentAttempt.");
        if (string.IsNullOrWhiteSpace(provedor))
            throw new RegraDeDominioVioladaException("Provedor e obrigatorio em PaymentAttempt.");
        if (string.IsNullOrWhiteSpace(metodo))
            throw new RegraDeDominioVioladaException("Metodo e obrigatorio em PaymentAttempt.");
        if (tentativa < 1)
            throw new RegraDeDominioVioladaException("Tentativa deve ser >= 1.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length != 64)
            throw new RegraDeDominioVioladaException("IdempotencyKey deve ser SHA-256 hex (64 chars).");

        return new PaymentAttempt
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            FaturaPagamentoId = faturaPagamentoId,
            FaturaId = faturaId,
            CobrancaAssinaturaId = cobrancaAssinaturaId,
            Provedor = provedor.Trim(),
            Metodo = metodo.Trim().ToLowerInvariant(),
            Status = StatusPaymentAttempt.Iniciado,
            Tentativa = tentativa,
            IniciadoEm = DateTime.UtcNow,
            IdempotencyKey = idempotencyKey,
            ClientIdempotencyKey = string.IsNullOrWhiteSpace(clientIdempotencyKey) ? null : clientIdempotencyKey.Trim(),
            RoutingMotivo = string.IsNullOrWhiteSpace(routingMotivo) ? "global-priority" : routingMotivo.Trim()
        };
    }

    /// <summary>
    /// Marca como sucesso com transactionId e payload do gateway. Idempotente
    /// — chamada repetida com mesmo Status e no-op (race webhook x sync response).
    /// </summary>
    public void MarcarSucesso(string? gatewayTransactionId, string? metadataJson = null, int? latenciaMs = null)
    {
        if (Status == StatusPaymentAttempt.Sucesso) return;
        if (Status is StatusPaymentAttempt.FalhaPermanente
                   or StatusPaymentAttempt.Recusado
                   or StatusPaymentAttempt.Cancelado)
            throw new RegraDeDominioVioladaException(
                $"Attempt em estado terminal '{Status}' nao pode virar Sucesso.");

        Status = StatusPaymentAttempt.Sucesso;
        FinalizadoEm = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(gatewayTransactionId))
            GatewayTransactionId = gatewayTransactionId.Trim();
        if (!string.IsNullOrWhiteSpace(metadataJson))
            MetadataJson = metadataJson;
        if (latenciaMs.HasValue) LatenciaMs = latenciaMs.Value;
        ErrorCategory = null;
        ErrorCode = null;
        ErrorMessage = null;
        ProximaConsultaEm = null;
    }

    /// <summary>
    /// Registra resposta do gateway (transactionId + metadata) sem mudar status.
    /// Usado pelo orchestrator quando <c>CriarAsync</c> retorna sucesso sincrono
    /// — o attempt continua em <see cref="StatusPaymentAttempt.Iniciado"/> ate o
    /// webhook (ou reconciliador em P1) confirmar o pagamento.
    /// </summary>
    public void RegistrarRespostaGateway(string? gatewayTransactionId, string? metadataJson, int? latenciaMs)
    {
        if (!string.IsNullOrWhiteSpace(gatewayTransactionId))
            GatewayTransactionId = gatewayTransactionId.Trim();
        if (!string.IsNullOrWhiteSpace(metadataJson))
            MetadataJson = metadataJson;
        if (latenciaMs.HasValue) LatenciaMs = latenciaMs.Value;
    }

    public void MarcarFalhaRetentavel(
        ErrorCategory categoria,
        string? errorCode,
        string? errorMessage,
        DateTime? proximaConsultaEm,
        int? latenciaMs = null)
    {
        if (Status is StatusPaymentAttempt.Sucesso
                   or StatusPaymentAttempt.FalhaPermanente
                   or StatusPaymentAttempt.Recusado
                   or StatusPaymentAttempt.Cancelado) return;

        Status = StatusPaymentAttempt.FalhaRetentavel;
        ErrorCategory = categoria;
        ErrorCode = errorCode?.Trim();
        ErrorMessage = TruncarMensagem(errorMessage);
        ProximaConsultaEm = proximaConsultaEm;
        if (latenciaMs.HasValue) LatenciaMs = latenciaMs.Value;
    }

    public void MarcarFalhaPermanente(
        ErrorCategory categoria,
        string? errorCode,
        string? errorMessage,
        int? latenciaMs = null)
    {
        if (Status == StatusPaymentAttempt.Sucesso) return;

        Status = StatusPaymentAttempt.FalhaPermanente;
        FinalizadoEm = DateTime.UtcNow;
        ErrorCategory = categoria;
        ErrorCode = errorCode?.Trim();
        ErrorMessage = TruncarMensagem(errorMessage);
        if (latenciaMs.HasValue) LatenciaMs = latenciaMs.Value;
        ProximaConsultaEm = null;
    }

    public void MarcarRecusado(string? errorCode, string? errorMessage, int? latenciaMs = null)
    {
        if (Status == StatusPaymentAttempt.Sucesso) return;
        Status = StatusPaymentAttempt.Recusado;
        FinalizadoEm = DateTime.UtcNow;
        ErrorCategory = Enums.Pagamentos.ErrorCategory.Declined;
        ErrorCode = errorCode?.Trim();
        ErrorMessage = TruncarMensagem(errorMessage);
        if (latenciaMs.HasValue) LatenciaMs = latenciaMs.Value;
    }

    public void MarcarCircuitOpen(int? latenciaMs = null)
    {
        if (Status == StatusPaymentAttempt.Sucesso) return;
        Status = StatusPaymentAttempt.CircuitOpen;
        FinalizadoEm = DateTime.UtcNow;
        ErrorCategory = Enums.Pagamentos.ErrorCategory.GatewayDown;
        if (latenciaMs.HasValue) LatenciaMs = latenciaMs.Value;
    }

    public void MarcarCancelado(string? motivo = null)
    {
        if (Status == StatusPaymentAttempt.Sucesso)
            throw new RegraDeDominioVioladaException("Attempt confirmado nao pode ser cancelado — use estorno.");
        Status = StatusPaymentAttempt.Cancelado;
        FinalizadoEm = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(motivo))
            ErrorMessage = TruncarMensagem(motivo);
    }

    public void RegistrarConsultaInconclusiva(DateTime? proximaConsultaEm = null)
    {
        if (Status == StatusPaymentAttempt.Sucesso) return;
        if (Status == StatusPaymentAttempt.Iniciado)
            Status = StatusPaymentAttempt.Inconclusivo;
        ProximaConsultaEm = proximaConsultaEm;
    }

    public void AgendarProximaConsulta(DateTime quando)
    {
        ProximaConsultaEm = quando;
    }

    private static string? TruncarMensagem(string? msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return null;
        msg = msg.Trim();
        return msg.Length > 500 ? msg.Substring(0, 500) : msg;
    }
}
