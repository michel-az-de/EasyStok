using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Domain.Entities.Financeiro;

/// <summary>
/// Parcela de uma <see cref="ContaPagar"/>. Recebe 1+N <see cref="PagamentoParcela"/>
/// (parcial ou total). Status agrega quando todos pagamentos sao confirmados.
/// </summary>
public class ParcelaPagar
{
    public Guid Id { get; set; }
    public Guid ContaPagarId { get; set; }

    /// <summary>Denormalizado pra index e tenant filter direto na parcela.</summary>
    public Guid EmpresaId { get; set; }

    public int Numero { get; set; }
    public decimal Valor { get; set; }
    public DateTime DataVencimento { get; set; }

    public decimal ValorPago { get; set; }
    public DateTime? DataPagamentoTotal { get; set; }

    public StatusParcela Status { get; set; } = StatusParcela.Pendente;
    public string? MetodoPlanejado { get; set; }

    // Idempotencia de notificacoes (job vencimento)
    public DateTime? NotificadaD3Em { get; set; }
    public DateTime? NotificadaD1Em { get; set; }
    public DateTime? NotificadaVencidaEm { get; set; }

    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    public uint Versao { get; set; }

    public ContaPagar? ContaPagar { get; set; }
    public ICollection<PagamentoParcela> Pagamentos { get; set; } = new List<PagamentoParcela>();

    public static ParcelaPagar Criar(
        Guid contaPagarId,
        Guid empresaId,
        int numero,
        decimal valor,
        DateTime dataVencimento,
        string? metodoPlanejado = null)
    {
        if (numero < 1)
            throw new RegraDeDominioVioladaException("Numero da parcela deve ser >= 1.");
        if (valor <= 0m)
            throw new RegraDeDominioVioladaException("Valor da parcela deve ser positivo.");

        var agora = DateTime.UtcNow;
        return new ParcelaPagar
        {
            Id = Guid.NewGuid(),
            ContaPagarId = contaPagarId,
            EmpresaId = empresaId,
            Numero = numero,
            Valor = Math.Round(valor, 2, MidpointRounding.AwayFromZero),
            DataVencimento = dataVencimento,
            ValorPago = 0m,
            Status = StatusParcela.Pendente,
            MetodoPlanejado = string.IsNullOrWhiteSpace(metodoPlanejado) ? null : metodoPlanejado.Trim().ToLowerInvariant(),
            CriadoEm = agora,
            AlteradoEm = agora
        };
    }

    public void RegistrarPagamento(PagamentoParcela pagamento)
    {
        if (pagamento is null) throw new ArgumentNullException(nameof(pagamento));
        if (Status == StatusParcela.Cancelada)
            throw new RegraDeDominioVioladaException("Nao e possivel registrar pagamento em parcela cancelada.");
        if (Status == StatusParcela.Paga)
            throw new RegraDeDominioVioladaException("Parcela ja paga — use estorno.");
        if (pagamento.EmpresaId != EmpresaId)
            throw new RegraDeDominioVioladaException("Pagamento pertence a outra empresa.");
        if (pagamento.Lado != TipoLadoFinanceiro.Pagar)
            throw new RegraDeDominioVioladaException("Pagamento e do lado contrario (esperado Pagar).");

        var totalConfirmadoApos = Pagamentos
            .Where(p => p.Status == StatusPagamentoParcela.Confirmado)
            .Sum(p => p.Valor)
            + (pagamento.Status == StatusPagamentoParcela.Confirmado ? pagamento.Valor : 0m);

        if (totalConfirmadoApos > Valor + 0.005m)
            throw new RegraDeDominioVioladaException("Soma dos pagamentos confirmados nao pode exceder o valor da parcela.");

        pagamento.ParcelaPagarId = Id;
        Pagamentos.Add(pagamento);
        AtualizarStatusPorPagamentos();
        AlteradoEm = DateTime.UtcNow;
    }

    public void AtualizarStatusPorPagamentos()
    {
        if (Status == StatusParcela.Cancelada) return;

        var confirmado = Pagamentos
            .Where(p => p.Status == StatusPagamentoParcela.Confirmado)
            .Sum(p => p.Valor);

        ValorPago = confirmado;

        if (confirmado >= Valor && Valor > 0m)
        {
            Status = StatusParcela.Paga;
            DataPagamentoTotal ??= DateTime.UtcNow;
        }
        else if (confirmado > 0m)
        {
            Status = StatusParcela.ParcialmentePaga;
            DataPagamentoTotal = null;
        }
        else if (DataVencimento.Date < DateTime.UtcNow.Date)
        {
            Status = StatusParcela.Vencida;
            DataPagamentoTotal = null;
        }
        else
        {
            Status = StatusParcela.Pendente;
            DataPagamentoTotal = null;
        }
    }

    public void MarcarVencidaSeAplicavel(DateTime hojeUtc)
    {
        if (Status != StatusParcela.Pendente && Status != StatusParcela.ParcialmentePaga) return;
        if (DataVencimento.Date >= hojeUtc.Date) return;
        Status = StatusParcela.Vencida;
        AlteradoEm = hojeUtc;
    }

    public void Cancelar()
    {
        if (Status == StatusParcela.Cancelada) return;
        if (Pagamentos.Any(p => p.Status == StatusPagamentoParcela.Confirmado))
            throw new RegraDeDominioVioladaException("Nao e possivel cancelar parcela com pagamento confirmado — use estorno antes.");
        Status = StatusParcela.Cancelada;
        AlteradoEm = DateTime.UtcNow;
    }

    public void CarimbarNotificacao(TipoEventoContaFinanceira evento, DateTime quandoUtc)
    {
        switch (evento)
        {
            case TipoEventoContaFinanceira.NotificadaD3:
                NotificadaD3Em = quandoUtc;
                break;
            case TipoEventoContaFinanceira.NotificadaD1:
                NotificadaD1Em = quandoUtc;
                break;
            case TipoEventoContaFinanceira.NotificadaVencida:
                NotificadaVencidaEm = quandoUtc;
                break;
        }
        AlteradoEm = quandoUtc;
    }

    public decimal Saldo
    {
        get
        {
            var s = Valor - ValorPago;
            return s < 0m ? 0m : s;
        }
    }
}
