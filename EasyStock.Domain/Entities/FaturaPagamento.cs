using System;
using EasyStock.Domain.Enums;
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
/// </summary>
public class FaturaPagamento
{
    public Guid Id { get; set; }
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

    public Fatura? Fatura { get; set; }

    public static FaturaPagamento CriarPendente(
        Guid faturaId,
        string metodo,
        decimal valor,
        string gatewayProvedor,
        string? gatewayTransactionId = null,
        string? dadosGatewayJson = null)
    {
        if (valor <= 0m)
            throw new RegraDeDominioVioladaException("Valor de pagamento deve ser maior que zero.");

        var agora = DateTime.UtcNow;
        return new FaturaPagamento
        {
            Id = Guid.NewGuid(),
            FaturaId = faturaId,
            Metodo = (metodo ?? "manual").Trim().ToLowerInvariant(),
            Valor = Math.Round(valor, 2, MidpointRounding.AwayFromZero),
            Status = StatusFaturaPagamento.Pendente,
            GatewayProvedor = gatewayProvedor,
            GatewayTransactionId = gatewayTransactionId,
            DadosGatewayJson = dadosGatewayJson,
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
        string? gatewayTransactionId = null,
        string? dadosGatewayJson = null,
        Guid? registradoPorUserId = null,
        string? registradoPorNome = null,
        string? observacao = null)
    {
        var p = CriarPendente(faturaId, metodo, valor, gatewayProvedor, gatewayTransactionId, dadosGatewayJson);
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
        if (Status != StatusFaturaPagamento.Pendente)
            throw new RegraDeDominioVioladaException($"So pagamentos pendentes podem ser confirmados (atual: {Status}).");
        Status = StatusFaturaPagamento.Confirmado;
        PagoEm = pagoEm ?? DateTime.UtcNow;
        AlteradoEm = DateTime.UtcNow;
    }

    public void MarcarFalhou(string? motivo = null)
    {
        if (Status == StatusFaturaPagamento.Falhou) return;
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
}
