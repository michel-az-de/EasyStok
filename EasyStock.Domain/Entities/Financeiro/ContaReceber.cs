using System;
using System.Collections.Generic;
using System.Linq;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Domain.Entities.Financeiro;

/// <summary>
/// Agregado raiz de "conta a receber" — receita operacional do tenant. Pode ser
/// avulsa ou vinculada a um <see cref="Pedido"/>. Tem 1+N <see cref="ParcelaReceber"/>
/// com vencimentos proprios e suporte opcional a Pix QR Code via <see cref="ParcelaReceber.EfiTxid"/>.
///
/// <para>
/// <see cref="FaturaId"/> e nullable para preparar geracao on-demand de
/// <see cref="Fatura"/> a partir de uma CR (escopo pos-MVP). Hoje sempre null.
/// </para>
/// </summary>
public class ContaReceber
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid? LojaId { get; set; }

    public Guid? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public Guid CategoriaFinanceiraId { get; set; }
    public CategoriaFinanceira? Categoria { get; set; }

    public Guid? CentroCustoId { get; set; }
    public CentroCusto? CentroCusto { get; set; }

    public string Descricao { get; set; } = null!;
    public string? Observacoes { get; set; }

    public decimal ValorTotal { get; set; }
    public StatusContaFinanceira Status { get; set; } = StatusContaFinanceira.Rascunho;

    public DateTime DataEmissao { get; set; }
    public DateTime? DataCompetencia { get; set; }

    public OrigemContaFinanceira Origem { get; set; } = OrigemContaFinanceira.Manual;
    public Guid? OrigemRefId { get; set; }
    public string? DocumentoReferencia { get; set; }

    /// <summary>Preparacao pos-MVP — gerar Fatura on-demand a partir desta CR. Hoje sempre null.</summary>
    public Guid? FaturaId { get; set; }
    public Fatura? Fatura { get; set; }

    public DateTime? CanceladaEm { get; set; }
    public Guid? CanceladaPorUserId { get; set; }
    public string? MotivoCancelamento { get; set; }

    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    public uint Versao { get; set; }

    public Empresa? Empresa { get; set; }
    public Loja? Loja { get; set; }

    public ICollection<ParcelaReceber> Parcelas { get; set; } = new List<ParcelaReceber>();
    public ICollection<ContaReceberAlteracao> Alteracoes { get; set; } = new List<ContaReceberAlteracao>();

    public static ContaReceber Criar(
        Guid empresaId,
        Guid? clienteId,
        Guid categoriaFinanceiraId,
        string descricao,
        DateTime dataEmissao,
        Guid? centroCustoId = null,
        Guid? lojaId = null,
        OrigemContaFinanceira origem = OrigemContaFinanceira.Manual,
        Guid? origemRefId = null,
        string? documentoReferencia = null,
        DateTime? dataCompetencia = null,
        string? observacoes = null)
    {
        if (string.IsNullOrWhiteSpace(descricao))
            throw new RegraDeDominioVioladaException("Descricao da conta a receber nao pode ser vazia.");
        if (categoriaFinanceiraId == Guid.Empty)
            throw new RegraDeDominioVioladaException("Categoria financeira e obrigatoria.");

        var agora = DateTime.UtcNow;
        return new ContaReceber
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            LojaId = lojaId,
            ClienteId = clienteId,
            CategoriaFinanceiraId = categoriaFinanceiraId,
            CentroCustoId = centroCustoId,
            Descricao = descricao.Trim(),
            DataEmissao = dataEmissao,
            DataCompetencia = dataCompetencia,
            Origem = origem,
            OrigemRefId = origemRefId,
            DocumentoReferencia = string.IsNullOrWhiteSpace(documentoReferencia) ? null : documentoReferencia.Trim(),
            Observacoes = string.IsNullOrWhiteSpace(observacoes) ? null : observacoes.Trim(),
            Status = StatusContaFinanceira.Rascunho,
            ValorTotal = 0m,
            CriadoEm = agora,
            AlteradoEm = agora
        };
    }

    public ParcelaReceber AdicionarParcela(int numero, decimal valor, DateTime dataVencimento, string? metodoPlanejado = null)
    {
        EnsureMutavel();
        if (Status != StatusContaFinanceira.Rascunho)
            throw new RegraDeDominioVioladaException("So e possivel adicionar parcelas em conta Rascunho.");
        if (numero < 1)
            throw new RegraDeDominioVioladaException("Numero da parcela deve ser >= 1.");
        if (Parcelas.Any(p => p.Numero == numero))
            throw new RegraDeDominioVioladaException($"Ja existe parcela com numero {numero}.");
        if (valor <= 0m)
            throw new RegraDeDominioVioladaException("Valor da parcela deve ser positivo.");

        var parcela = ParcelaReceber.Criar(Id, EmpresaId, numero, valor, dataVencimento, metodoPlanejado);
        Parcelas.Add(parcela);
        RecalcularValorTotal();
        AlteradoEm = DateTime.UtcNow;
        return parcela;
    }

    public void RemoverParcela(Guid parcelaId)
    {
        EnsureMutavel();
        if (Status != StatusContaFinanceira.Rascunho)
            throw new RegraDeDominioVioladaException("So e possivel remover parcelas em conta Rascunho.");
        var p = Parcelas.FirstOrDefault(x => x.Id == parcelaId)
                ?? throw new RegraDeDominioVioladaException("Parcela nao encontrada.");
        Parcelas.Remove(p);
        RecalcularValorTotal();
        AlteradoEm = DateTime.UtcNow;
    }

    public void RecalcularValorTotal()
    {
        ValorTotal = Parcelas
            .Where(p => p.Status != StatusParcela.Cancelada)
            .Sum(p => p.Valor);
    }

    public void Emitir()
    {
        if (Status == StatusContaFinanceira.Aberta) return;
        if (Status != StatusContaFinanceira.Rascunho)
            throw new RegraDeDominioVioladaException($"So Rascunho pode ser emitida (status atual: {Status}).");
        if (Parcelas.Count == 0)
            throw new RegraDeDominioVioladaException("Conta sem parcelas nao pode ser emitida.");
        if (ValorTotal <= 0m)
            throw new RegraDeDominioVioladaException("Valor total deve ser positivo pra emitir.");

        Status = StatusContaFinanceira.Aberta;
        AlteradoEm = DateTime.UtcNow;
    }

    public void Cancelar(string motivo, Guid? userId)
    {
        if (Status == StatusContaFinanceira.Cancelada) return;
        if (Status == StatusContaFinanceira.Paga)
            throw new RegraDeDominioVioladaException("Conta paga nao pode ser cancelada — use estorno de pagamentos.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new RegraDeDominioVioladaException("Motivo do cancelamento e obrigatorio.");

        foreach (var p in Parcelas.Where(p => p.Status != StatusParcela.Paga && p.Status != StatusParcela.Cancelada))
            p.Cancelar();

        Status = StatusContaFinanceira.Cancelada;
        CanceladaEm = DateTime.UtcNow;
        CanceladaPorUserId = userId;
        MotivoCancelamento = motivo.Trim();
        AlteradoEm = DateTime.UtcNow;
    }

    public void AtualizarStatusPorParcelas()
    {
        if (Status == StatusContaFinanceira.Cancelada || Status == StatusContaFinanceira.Rascunho) return;

        var ativas = Parcelas.Where(p => p.Status != StatusParcela.Cancelada).ToList();
        if (ativas.Count == 0)
        {
            Status = StatusContaFinanceira.Cancelada;
            return;
        }

        var pagas = ativas.Count(p => p.Status == StatusParcela.Paga);
        var algumaVencida = ativas.Any(p => p.Status == StatusParcela.Vencida);
        var algumPagamento = ativas.Any(p => p.ValorPago > 0m);

        Status = pagas == ativas.Count
            ? StatusContaFinanceira.Paga
            : algumaVencida
                ? StatusContaFinanceira.Vencida
                : algumPagamento
                    ? StatusContaFinanceira.ParcialmentePaga
                    : StatusContaFinanceira.Aberta;

        AlteradoEm = DateTime.UtcNow;
    }

    public void MarcarVencidaSeAplicavel(DateTime hojeUtc)
    {
        if (Status != StatusContaFinanceira.Aberta && Status != StatusContaFinanceira.ParcialmentePaga) return;
        var alguma = Parcelas.Any(p =>
            p.Status != StatusParcela.Paga &&
            p.Status != StatusParcela.Cancelada &&
            p.DataVencimento.Date < hojeUtc.Date);
        if (alguma)
        {
            Status = StatusContaFinanceira.Vencida;
            AlteradoEm = DateTime.UtcNow;
        }
    }

    public decimal TotalRecebido => Parcelas.Where(p => p.Status != StatusParcela.Cancelada).Sum(p => p.ValorPago);
    public decimal Pendente
    {
        get
        {
            var saldo = ValorTotal - TotalRecebido;
            return saldo < 0m ? 0m : saldo;
        }
    }

    private void EnsureMutavel()
    {
        if (Status == StatusContaFinanceira.Cancelada)
            throw new RegraDeDominioVioladaException("Conta cancelada e imutavel.");
        if (Status == StatusContaFinanceira.Paga)
            throw new RegraDeDominioVioladaException("Conta paga e imutavel — use estorno.");
    }
}

/// <summary>Audit por campo de alteracao em <see cref="ContaReceber"/>.</summary>
public class ContaReceberAlteracao
{
    public Guid Id { get; set; }
    public Guid ContaReceberId { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid? AlteradoPorUserId { get; set; }
    public string? AlteradoPorNome { get; set; }
    public string Campo { get; set; } = null!;
    public string? ValorAntigo { get; set; }
    public string? ValorNovo { get; set; }
    public DateTime AlteradoEm { get; set; }
    public string? Origem { get; set; }

    public ContaReceber? ContaReceber { get; set; }
}
