using System;
using System.Collections.Generic;
using System.Linq;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities;

/// <summary>
/// <para>
/// Agregado raiz do modulo financeiro. Representa um documento de cobranca
/// agnostico de origem (assinatura SaaS, pedido ERP, fatura avulsa).
/// </para>
///
/// <para>
/// <b>Convivencia:</b> ate F11, esta entidade convive com <see cref="CobrancaAssinatura"/>.
/// Quando uma cobranca de assinatura e gerada, uma <see cref="Fatura"/> e criada
/// junto com <see cref="OrigemFatura.Assinatura"/> e <see cref="OrigemRefId"/>
/// apontando para <see cref="AssinaturaEmpresa"/>; <see cref="CobrancaAssinatura.FaturaId"/>
/// linka de volta. A Fatura e a fonte da verdade do valor devido — a Cobranca
/// e snapshot do gateway (Pix/Boleto).
/// </para>
///
/// <para>
/// <b>Snapshot:</b> <see cref="DadosFaturado"/> e <see cref="DadosEmissor"/> sao
/// gravados como JSON no momento da emissao para preservar exatamente o que foi
/// mostrado/cobrado, mesmo se Cliente ou Empresa forem editados depois.
/// </para>
///
/// <para>
/// <b>Concorrencia:</b> <see cref="Versao"/> e usado como rowversion para
/// optimistic concurrency entre o job de cobranca e o webhook (que podem
/// atualizar a mesma fatura simultaneamente).
/// </para>
/// </summary>
public class Fatura
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }

    /// <summary>
    /// Numero sequencial fiscal-friendly por empresa+ano. Formato: <c>YYYY-NNNNNN</c>
    /// (ex: <c>2026-000042</c>). Gerado por <see cref="FaturaContador"/>.
    /// </summary>
    public string Numero { get; set; } = null!;

    public Guid? ClienteId { get; set; }

    /// <summary>Snapshot JSON do destinatario (faturado).</summary>
    public DadosFaturado DadosFaturado { get; set; } = null!;

    /// <summary>Snapshot JSON do emissor (empresa).</summary>
    public DadosEmissor DadosEmissor { get; set; } = null!;

    /// <summary>Dados fiscais opcionais — preparados para NFS-e futura.</summary>
    public DadosFiscais? DadosFiscais { get; set; }

    public OrigemFatura Origem { get; set; }
    public Guid? OrigemRefId { get; set; }

    public StatusFatura Status { get; set; } = StatusFatura.Rascunho;

    public DateTime DataEmissao { get; set; }
    public DateTime DataVencimento { get; set; }
    public DateTime? DataPagamentoTotal { get; set; }

    public decimal SubTotal { get; set; }
    public decimal Descontos { get; set; }
    public decimal Acrescimos { get; set; }
    public decimal Total { get; set; }
    public string Moeda { get; set; } = "BRL";

    public string? Observacoes { get; set; }
    public string? MetadataJson { get; set; }

    /// <summary>FK opcional a um <see cref="AdminTicket"/> primario (helpdesk financeiro).</summary>
    public Guid? TicketRelacionadoId { get; set; }

    /// <summary>RowVersion para optimistic concurrency (mapeada como xmin no PG).</summary>
    public uint Versao { get; set; }

    /// <summary>Chave do PDF cacheado em <c>IFileStorage</c> (S3/disco). Null = ainda nao gerado.</summary>
    public string? PdfStorageKey { get; set; }

    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    // Navegacao
    public Empresa? Empresa { get; set; }
    public Cliente? Cliente { get; set; }
    public AdminTicket? TicketRelacionado { get; set; }
    public ICollection<FaturaItem> Itens { get; set; } = new List<FaturaItem>();
    public ICollection<FaturaPagamento> Pagamentos { get; set; } = new List<FaturaPagamento>();
    public ICollection<FaturaEvento> Eventos { get; set; } = new List<FaturaEvento>();

    // ─── Construcao ─────────────────────────────────────────────────────

    /// <summary>
    /// Cria uma fatura em estado <see cref="StatusFatura.Rascunho"/>. Os itens
    /// devem ser adicionados via <see cref="AdicionarItem"/> antes de chamar
    /// <see cref="Emitir"/>.
    /// </summary>
    public static Fatura Criar(
        Guid empresaId,
        string numero,
        DadosFaturado dadosFaturado,
        DadosEmissor dadosEmissor,
        OrigemFatura origem,
        DateTime dataEmissao,
        DateTime dataVencimento,
        Guid? clienteId = null,
        Guid? origemRefId = null,
        string? observacoes = null,
        string moeda = "BRL")
    {
        if (string.IsNullOrWhiteSpace(numero))
            throw new ArgumentException("Numero da fatura nao pode ser vazio.", nameof(numero));
        if (dadosFaturado is null)
            throw new ArgumentNullException(nameof(dadosFaturado));
        if (dadosEmissor is null)
            throw new ArgumentNullException(nameof(dadosEmissor));
        if (dataVencimento.Date < dataEmissao.Date)
            throw new RegraDeDominioVioladaException("Data de vencimento nao pode ser anterior a data de emissao.");

        var agora = DateTime.UtcNow;
        return new Fatura
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Numero = numero,
            ClienteId = clienteId,
            DadosFaturado = dadosFaturado,
            DadosEmissor = dadosEmissor,
            Origem = origem,
            OrigemRefId = origemRefId,
            Status = StatusFatura.Rascunho,
            DataEmissao = dataEmissao,
            DataVencimento = dataVencimento,
            Moeda = moeda,
            Observacoes = observacoes,
            CriadoEm = agora,
            AlteradoEm = agora
        };
    }

    // ─── Itens ───────────────────────────────────────────────────────────

    public FaturaItem AdicionarItem(
        string descricao,
        decimal quantidade,
        decimal precoUnitario,
        TipoItemFatura tipo = TipoItemFatura.Servico)
    {
        EnsureMutavel();
        var item = FaturaItem.Criar(Id, descricao, quantidade, precoUnitario, tipo, ordem: Itens.Count);
        Itens.Add(item);
        RecalcularTotais();
        AlteradoEm = DateTime.UtcNow;
        return item;
    }

    public void RemoverItem(Guid faturaItemId)
    {
        EnsureMutavel();
        var item = Itens.FirstOrDefault(i => i.Id == faturaItemId)
            ?? throw new RegraDeDominioVioladaException("Item nao encontrado na fatura.");
        Itens.Remove(item);
        RecalcularTotais();
        AlteradoEm = DateTime.UtcNow;
    }

    public void RecalcularTotais()
    {
        decimal sub = 0m, desc = 0m, acr = 0m;
        foreach (var i in Itens)
        {
            switch (i.Tipo)
            {
                case TipoItemFatura.Desconto:
                    desc += Math.Abs(i.Subtotal);
                    break;
                case TipoItemFatura.Taxa:
                    acr += i.Subtotal;
                    sub += i.Subtotal;
                    break;
                default:
                    sub += i.Subtotal;
                    break;
            }
        }
        SubTotal = sub - acr; // taxa nao deve compor SubTotal "limpo"
        Descontos = desc;
        Acrescimos = acr;
        Total = SubTotal - Descontos + Acrescimos;
        if (Total < 0) Total = 0m;
    }

    // ─── Transicoes de status ────────────────────────────────────────────

    /// <summary>Transiciona Rascunho → Emitida. Idempotente para Emitida.</summary>
    public void Emitir()
    {
        if (Status == StatusFatura.Emitida) return;
        if (Status != StatusFatura.Rascunho)
            throw new RegraDeDominioVioladaException($"So Rascunho pode ser emitida (status atual: {Status}).");
        if (Itens.Count == 0)
            throw new RegraDeDominioVioladaException("Fatura sem itens nao pode ser emitida.");

        Status = StatusFatura.Emitida;
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Registra pagamento e ajusta status. Se soma de pagamentos confirmados &gt;= Total,
    /// marca como <see cref="StatusFatura.Paga"/>. Senao <see cref="StatusFatura.ParcialmentePaga"/>.
    /// </summary>
    public void RegistrarPagamento(FaturaPagamento pagamento)
    {
        if (pagamento is null) throw new ArgumentNullException(nameof(pagamento));
        if (Status == StatusFatura.Cancelada)
            throw new RegraDeDominioVioladaException("Nao e possivel registrar pagamento em fatura cancelada.");
        if (Status == StatusFatura.Rascunho)
            throw new RegraDeDominioVioladaException("Emita a fatura antes de registrar pagamento.");

        pagamento.FaturaId = Id;
        Pagamentos.Add(pagamento);
        AtualizarStatusPorPagamentos();
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>Recalcula status a partir dos pagamentos confirmados.</summary>
    public void AtualizarStatusPorPagamentos()
    {
        if (Status == StatusFatura.Cancelada || Status == StatusFatura.Rascunho)
            return;

        var confirmados = Pagamentos
            .Where(p => p.Status == StatusFaturaPagamento.Confirmado)
            .Sum(p => p.Valor);

        if (confirmados >= Total && Total > 0m)
        {
            Status = StatusFatura.Paga;
            DataPagamentoTotal ??= DateTime.UtcNow;
        }
        else if (confirmados > 0m)
        {
            Status = StatusFatura.ParcialmentePaga;
        }
        else if (DataVencimento.Date < DateTime.UtcNow.Date)
        {
            Status = StatusFatura.Vencida;
        }
        else
        {
            Status = StatusFatura.Emitida;
        }
    }

    /// <summary>Marca como vencida se passou da data e nao foi paga. Job chama isto.</summary>
    public void MarcarVencidaSeAplicavel()
    {
        if (Status != StatusFatura.Emitida && Status != StatusFatura.ParcialmentePaga) return;
        if (DataVencimento.Date >= DateTime.UtcNow.Date) return;
        Status = StatusFatura.Vencida;
        AlteradoEm = DateTime.UtcNow;
    }

    public void Cancelar(string? motivo = null)
    {
        if (Status == StatusFatura.Cancelada) return;
        if (Status == StatusFatura.Paga)
            throw new RegraDeDominioVioladaException("Fatura paga nao pode ser cancelada — use estorno.");

        Status = StatusFatura.Cancelada;
        if (!string.IsNullOrWhiteSpace(motivo))
            Observacoes = string.IsNullOrWhiteSpace(Observacoes) ? motivo : $"{Observacoes}\n[Cancelamento] {motivo}";
        AlteradoEm = DateTime.UtcNow;
    }

    public decimal TotalPago => Pagamentos
        .Where(p => p.Status == StatusFaturaPagamento.Confirmado)
        .Sum(p => p.Valor);

    public decimal Pendente
    {
        get
        {
            var saldo = Total - TotalPago;
            return saldo < 0m ? 0m : saldo;
        }
    }

    public bool VinculaTicket(Guid ticketId)
    {
        if (TicketRelacionadoId == ticketId) return false;
        TicketRelacionadoId = ticketId;
        AlteradoEm = DateTime.UtcNow;
        return true;
    }

    private void EnsureMutavel()
    {
        if (Status == StatusFatura.Cancelada)
            throw new RegraDeDominioVioladaException("Fatura cancelada e imutavel.");
        if (Status == StatusFatura.Paga)
            throw new RegraDeDominioVioladaException("Fatura paga e imutavel — emita uma nova.");
    }
}
