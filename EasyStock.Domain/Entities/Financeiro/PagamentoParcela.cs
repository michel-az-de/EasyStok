using System;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Domain.Entities.Financeiro;

/// <summary>
/// Pagamento de uma <see cref="ParcelaPagar"/> ou <see cref="ParcelaReceber"/>.
/// Polimorfico via XOR entre <see cref="ParcelaPagarId"/> e <see cref="ParcelaReceberId"/>:
/// exatamente um deles deve estar preenchido (check constraint no DB).
///
/// <para>
/// <see cref="MovimentoCaixaId"/> referencia o <see cref="MovimentoCaixa"/> criado
/// na MESMA transacao do registro do pagamento — idempotencia cruzada com Caixa.
/// Estorno simetrico estorna o movimento.
/// </para>
/// </summary>
public class PagamentoParcela
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }

    public TipoLadoFinanceiro Lado { get; set; }

    public Guid? ParcelaPagarId { get; set; }
    public ParcelaPagar? ParcelaPagar { get; set; }

    public Guid? ParcelaReceberId { get; set; }
    public ParcelaReceber? ParcelaReceber { get; set; }

    public decimal Valor { get; set; }
    public string Metodo { get; set; } = "outro";

    public StatusPagamentoParcela Status { get; set; } = StatusPagamentoParcela.Pendente;

    public DateTime DataPagamento { get; set; }

    public string? GatewayProvedor { get; set; } // "Manual" | "EfiPix" | "Stripe" | ...
    public string? GatewayTransactionId { get; set; }
    public string? DadosGatewayJson { get; set; }

    public Guid? RegistradoPorUserId { get; set; }
    public string? RegistradoPorNome { get; set; }
    public string? Observacao { get; set; }

    /// <summary>FK pro MovimentoCaixa criado na mesma transacao (idempotencia cruzada).</summary>
    public Guid? MovimentoCaixaId { get; set; }
    public MovimentoCaixa? MovimentoCaixa { get; set; }

    public DateTime? EstornadoEm { get; set; }
    public Guid? EstornadoPorUserId { get; set; }
    public string? MotivoEstorno { get; set; }

    public DateTime CriadoEm { get; set; }

    public static PagamentoParcela CriarConfirmado(
        Guid empresaId,
        TipoLadoFinanceiro lado,
        decimal valor,
        string metodo,
        DateTime dataPagamento,
        string gatewayProvedor = "Manual",
        string? gatewayTransactionId = null,
        string? observacao = null,
        Guid? registradoPorUserId = null,
        string? registradoPorNome = null)
    {
        if (valor <= 0m) throw new RegraDeDominioVioladaException("Valor do pagamento deve ser positivo.");
        if (string.IsNullOrWhiteSpace(metodo)) throw new RegraDeDominioVioladaException("Metodo do pagamento e obrigatorio.");

        return new PagamentoParcela
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Lado = lado,
            Valor = Math.Round(valor, 2, MidpointRounding.AwayFromZero),
            Metodo = metodo.Trim().ToLowerInvariant(),
            Status = StatusPagamentoParcela.Confirmado,
            DataPagamento = dataPagamento,
            GatewayProvedor = string.IsNullOrWhiteSpace(gatewayProvedor) ? "Manual" : gatewayProvedor.Trim(),
            GatewayTransactionId = string.IsNullOrWhiteSpace(gatewayTransactionId) ? null : gatewayTransactionId.Trim(),
            Observacao = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim(),
            RegistradoPorUserId = registradoPorUserId,
            RegistradoPorNome = registradoPorNome,
            CriadoEm = DateTime.UtcNow
        };
    }

    public static PagamentoParcela CriarPendente(
        Guid empresaId,
        TipoLadoFinanceiro lado,
        decimal valor,
        string metodo,
        DateTime dataPagamento,
        string gatewayProvedor,
        string gatewayTransactionId,
        string? observacao = null)
    {
        if (valor <= 0m) throw new RegraDeDominioVioladaException("Valor do pagamento deve ser positivo.");
        if (string.IsNullOrWhiteSpace(gatewayProvedor)) throw new RegraDeDominioVioladaException("Gateway e obrigatorio em pagamento pendente.");
        if (string.IsNullOrWhiteSpace(gatewayTransactionId)) throw new RegraDeDominioVioladaException("TransactionId e obrigatorio em pagamento pendente.");

        return new PagamentoParcela
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Lado = lado,
            Valor = Math.Round(valor, 2, MidpointRounding.AwayFromZero),
            Metodo = metodo.Trim().ToLowerInvariant(),
            Status = StatusPagamentoParcela.Pendente,
            DataPagamento = dataPagamento,
            GatewayProvedor = gatewayProvedor.Trim(),
            GatewayTransactionId = gatewayTransactionId.Trim(),
            Observacao = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim(),
            CriadoEm = DateTime.UtcNow
        };
    }

    public void Confirmar()
    {
        if (Status == StatusPagamentoParcela.Confirmado) return;
        if (Status != StatusPagamentoParcela.Pendente)
            throw new RegraDeDominioVioladaException($"So pendente pode ser confirmado (atual: {Status}).");
        Status = StatusPagamentoParcela.Confirmado;
    }

    public void MarcarFalhou(string? motivo = null)
    {
        if (Status == StatusPagamentoParcela.Falhou) return;
        if (Status != StatusPagamentoParcela.Pendente)
            throw new RegraDeDominioVioladaException($"So pendente pode falhar (atual: {Status}).");
        Status = StatusPagamentoParcela.Falhou;
        if (!string.IsNullOrWhiteSpace(motivo)) Observacao = motivo.Trim();
    }

    public void Estornar(Guid? userId, string? motivo)
    {
        if (Status == StatusPagamentoParcela.Estornado) return;
        if (Status != StatusPagamentoParcela.Confirmado)
            throw new RegraDeDominioVioladaException($"So confirmado pode ser estornado (atual: {Status}).");
        Status = StatusPagamentoParcela.Estornado;
        EstornadoEm = DateTime.UtcNow;
        EstornadoPorUserId = userId;
        MotivoEstorno = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
    }

    public void AssociarMovimentoCaixa(Guid movimentoCaixaId)
    {
        if (movimentoCaixaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("MovimentoCaixa invalido.");
        MovimentoCaixaId = movimentoCaixaId;
    }
}
