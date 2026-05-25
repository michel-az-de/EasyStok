using System;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Domain.Entities;

/// <summary>
/// Tentativa/registro de pagamento de uma <see cref="Fatura"/>. Pode haver
/// multiplos por fatura (parciais, estornos, falhas com retry).
///
/// <para>
/// <see cref="DadosGatewayJson"/> guarda a resposta completa do gateway
/// (pix_copia_cola, qr_code_base64, boleto_url, etc.) — formato livre, depende
/// do <see cref="GatewayProvedor"/>. Renderer PDF e UI extraem campos relevantes
/// por nome via JsonDocument.
/// </para>
///
/// <para>
/// <b>Onda P0 Payment Orchestration</b>: <see cref="EmpresaId"/> desnormalizado
/// (de <see cref="Fatura.EmpresaId"/>) para Global Query Filter direto e
/// queries sem JOIN. <see cref="TentativaAtualId"/>, <see cref="TotalTentativas"/>
/// e <see cref="UltimaErrorCategory"/> sao agregados denormalizados de
/// <c>PaymentAttempt</c> para dashboards e UI rapidos.
/// </para>
///
/// <para>
/// <b>Idempotencia da intencao</b>: <see cref="ClientIdempotencyKey"/> opcional
/// recebido em header <c>Idempotency-Key</c> da request. UNIQUE parcial por
/// (<see cref="EmpresaId"/>, <see cref="ClientIdempotencyKey"/>) garante que
/// segunda chamada com mesma key retorna o mesmo pagamento (sem criar 2).
/// </para>
/// </summary>
public class FaturaPagamento
{
    public Guid Id { get; set; }

    /// <summary>Desnormalizado de <see cref="Fatura.EmpresaId"/> (Onda P0 Orchestration).</summary>
    public Guid EmpresaId { get; set; }

    public Guid FaturaId { get; set; }

    /// <summary>"pix" | "boleto" | "cartao" | "dinheiro" | "manual" | string aberta.</summary>
    public string Metodo { get; set; } = "manual";

    public decimal Valor { get; set; }
    public StatusFaturaPagamento Status { get; set; } = StatusFaturaPagamento.Pendente;

    /// <summary>"EfiPix" | "EfiBoleto" | "Manual" | "Stripe" | etc.</summary>
    public string GatewayProvedor { get; set; } = "Manual";

    /// <summary>txid Pix, NSU cartao, ID Stripe, etc.</summary>
    public string? GatewayTransactionId { get; set; }

    /// <summary>Dump bruto do gateway (jsonb). Renderer/UI extraem campos por nome.</summary>
    public string? DadosGatewayJson { get; set; }

    public DateTime? PagoEm { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    public Guid? RegistradoPorUserId { get; set; }
    public string? RegistradoPorNome { get; set; }
    public string? Observacao { get; set; }

    /// <summary>FK ao <c>PaymentAttempt</c> corrente (null se ainda nao iniciado pelo orchestrator).</summary>
    public Guid? TentativaAtualId { get; set; }

    /// <summary>Quantidade de attempts ja criados (incrementa a cada nova tentativa).</summary>
    public int TotalTentativas { get; set; }

    /// <summary>Categoria do erro da ultima tentativa, denormalizada para query rapida.</summary>
    public ErrorCategory? UltimaErrorCategory { get; set; }

    /// <summary>Header <c>Idempotency-Key</c> opcional do cliente. UNIQUE parcial por (EmpresaId, ClientIdempotencyKey).</summary>
    public string? ClientIdempotencyKey { get; set; }

    public Fatura? Fatura { get; set; }

    public static FaturaPagamento CriarPendente(
        Guid faturaId,
        string metodo,
        decimal valor,
        string gatewayProvedor,
        Guid empresaId,
        string? gatewayTransactionId = null,
        string? dadosGatewayJson = null,
        string? clientIdempotencyKey = null)
    {
        if (valor <= 0m)
            throw new RegraDeDominioVioladaException("Valor de pagamento deve ser maior que zero.");
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId e obrigatorio em FaturaPagamento.");

        var agora = DateTime.UtcNow;
        return new FaturaPagamento
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            FaturaId = faturaId,
            Metodo = (metodo ?? "manual").Trim().ToLowerInvariant(),
            Valor = Math.Round(valor, 2, MidpointRounding.AwayFromZero),
            Status = StatusFaturaPagamento.Pendente,
            GatewayProvedor = gatewayProvedor,
            GatewayTransactionId = gatewayTransactionId,
            DadosGatewayJson = dadosGatewayJson,
            ClientIdempotencyKey = string.IsNullOrWhiteSpace(clientIdempotencyKey) ? null : clientIdempotencyKey.Trim(),
            CriadoEm = agora,
            AlteradoEm = agora
        };
    }

    /// <summary>
    /// Cria um pagamento ja confirmado (uso: pagamento manual em dinheiro,
    /// webhook que chega antes de Pendente ser persistido).
    /// </summary>
    public static FaturaPagamento CriarConfirmado(
        Guid faturaId,
        string metodo,
        decimal valor,
        string gatewayProvedor,
        Guid empresaId,
        string? gatewayTransactionId = null,
        string? dadosGatewayJson = null,
        Guid? registradoPorUserId = null,
        string? registradoPorNome = null,
        string? observacao = null)
    {
        var p = CriarPendente(faturaId, metodo, valor, gatewayProvedor, empresaId, gatewayTransactionId, dadosGatewayJson);
        p.Status = StatusFaturaPagamento.Confirmado;
        p.PagoEm = DateTime.UtcNow;
        p.RegistradoPorUserId = registradoPorUserId;
        p.RegistradoPorNome = registradoPorNome;
        p.Observacao = observacao;
        return p;
    }

    public void Confirmar(DateTime? pagoEm = null)
    {
        if (Status == StatusFaturaPagamento.Confirmado) return;
        if (Status != StatusFaturaPagamento.Pendente && Status != StatusFaturaPagamento.EmProcessamento)
            throw new RegraDeDominioVioladaException(
                $"So pagamentos Pendente ou EmProcessamento podem ser confirmados (atual: {Status}).");
        Status = StatusFaturaPagamento.Confirmado;
        PagoEm = pagoEm ?? DateTime.UtcNow;
        AlteradoEm = DateTime.UtcNow;
    }

    public void MarcarFalhou(string? motivo = null)
    {
        if (Status == StatusFaturaPagamento.Falhou) return;
        if (Status == StatusFaturaPagamento.Confirmado)
            throw new RegraDeDominioVioladaException("Pagamento Confirmado nao pode virar Falhou — use estorno.");
        Status = StatusFaturaPagamento.Falhou;
        if (!string.IsNullOrWhiteSpace(motivo))
            Observacao = string.IsNullOrWhiteSpace(Observacao) ? motivo : $"{Observacao}\n{motivo}";
        AlteradoEm = DateTime.UtcNow;
    }

    public void SolicitarEstorno()
    {
        if (Status != StatusFaturaPagamento.Confirmado)
            throw new RegraDeDominioVioladaException("So pagamentos confirmados podem ser estornados.");
        Status = StatusFaturaPagamento.EstornoSolicitado;
        AlteradoEm = DateTime.UtcNow;
    }

    public void ConfirmarEstorno()
    {
        if (Status != StatusFaturaPagamento.EstornoSolicitado)
            throw new RegraDeDominioVioladaException("Estorno deve ter sido solicitado antes de confirmado.");
        Status = StatusFaturaPagamento.Estornado;
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Marca a transicao de <see cref="StatusFaturaPagamento.Pendente"/> para
    /// <see cref="StatusFaturaPagamento.EmProcessamento"/> quando o orchestrator
    /// inicia um attempt em algum gateway. Idempotente.
    /// </summary>
    public void MarcarEmProcessamento(string? novoGatewayProvedor = null)
    {
        if (Status == StatusFaturaPagamento.EmProcessamento) return;
        if (Status != StatusFaturaPagamento.Pendente)
            throw new RegraDeDominioVioladaException(
                $"So pagamentos Pendente podem ir para EmProcessamento (atual: {Status}).");
        Status = StatusFaturaPagamento.EmProcessamento;
        if (!string.IsNullOrWhiteSpace(novoGatewayProvedor))
            GatewayProvedor = novoGatewayProvedor.Trim();
        AlteradoEm = DateTime.UtcNow;
    }

    public void MarcarCancelado(string? motivo = null)
    {
        if (Status == StatusFaturaPagamento.Cancelado) return;
        if (Status == StatusFaturaPagamento.Confirmado || Status == StatusFaturaPagamento.Estornado)
            throw new RegraDeDominioVioladaException(
                $"Pagamento em estado terminal '{Status}' nao pode ser cancelado.");
        Status = StatusFaturaPagamento.Cancelado;
        if (!string.IsNullOrWhiteSpace(motivo))
            Observacao = string.IsNullOrWhiteSpace(Observacao) ? motivo : $"{Observacao}\n{motivo}";
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Atualiza o ponteiro para o <c>PaymentAttempt</c> corrente e incrementa
    /// <see cref="TotalTentativas"/>. Chamado pelo orchestrator ao criar nova
    /// tentativa.
    /// </summary>
    public void RegistrarNovaTentativa(Guid attemptId, int tentativaNumero)
    {
        if (attemptId == Guid.Empty)
            throw new RegraDeDominioVioladaException("AttemptId e obrigatorio.");
        TentativaAtualId = attemptId;
        TotalTentativas = tentativaNumero;
        UltimaErrorCategory = null;
        AlteradoEm = DateTime.UtcNow;
    }

    public void RegistrarErroTentativa(ErrorCategory categoria)
    {
        UltimaErrorCategory = categoria;
        AlteradoEm = DateTime.UtcNow;
    }
}
