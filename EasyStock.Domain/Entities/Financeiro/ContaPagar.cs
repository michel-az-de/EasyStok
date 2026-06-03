using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Domain.Entities.Financeiro;

/// <summary>
/// Agregado raiz de "conta a pagar" — despesa operacional do tenant. Pode ser
/// avulsa (ex: aluguel, conta de luz) ou vinculada a um <see cref="PedidoFornecedor"/>.
/// Tem 1+N <see cref="ParcelaPagar"/> com vencimentos proprios.
/// </summary>
public class ContaPagar
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid? LojaId { get; set; }

    /// <summary>FK opcional. Null = despesa avulsa sem fornecedor cadastrado.</summary>
    public Guid? FornecedorId { get; set; }
    public Fornecedor? Fornecedor { get; set; }

    public Guid CategoriaFinanceiraId { get; set; }
    public CategoriaFinanceira? Categoria { get; set; }

    public Guid? CentroCustoId { get; set; }
    public CentroCusto? CentroCusto { get; set; }

    public string Descricao { get; set; } = null!;
    public string? Observacoes { get; set; }

    /// <summary>Soma de <c>parcelas.Valor</c> das parcelas nao canceladas.</summary>
    public decimal ValorTotal { get; set; }

    public StatusContaFinanceira Status { get; set; } = StatusContaFinanceira.Rascunho;

    public DateTime DataEmissao { get; set; }

    /// <summary>Data de competencia contabil. Null = mesma da emissao.</summary>
    public DateTime? DataCompetencia { get; set; }

    // Origem (idempotencia ao gerar via integracao)
    public OrigemContaFinanceira Origem { get; set; } = OrigemContaFinanceira.Manual;
    public Guid? OrigemRefId { get; set; }
    public string? DocumentoReferencia { get; set; }

    // Cancelamento (soft)
    public DateTime? CanceladaEm { get; set; }
    public Guid? CanceladaPorUserId { get; set; }
    public string? MotivoCancelamento { get; set; }

    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    /// <summary>RowVersion mapeada como xmin (PG system column).</summary>
    public uint Versao { get; set; }

    public Empresa? Empresa { get; set; }
    public Loja? Loja { get; set; }

    public ICollection<ParcelaPagar> Parcelas { get; set; } = new List<ParcelaPagar>();
    public ICollection<ContaPagarAlteracao> Alteracoes { get; set; } = new List<ContaPagarAlteracao>();

    public static ContaPagar Criar(
        Guid empresaId,
        Guid? fornecedorId,
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
            throw new RegraDeDominioVioladaException("Descricao da conta a pagar nao pode ser vazia.");
        if (categoriaFinanceiraId == Guid.Empty)
            throw new RegraDeDominioVioladaException("Categoria financeira e obrigatoria.");

        var agora = DateTime.UtcNow;
        return new ContaPagar
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            LojaId = lojaId,
            FornecedorId = fornecedorId,
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

    public ParcelaPagar AdicionarParcela(int numero, decimal valor, DateTime dataVencimento, string? metodoPlanejado = null)
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

        var parcela = ParcelaPagar.Criar(Id, EmpresaId, numero, valor, dataVencimento, metodoPlanejado);
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

    /// <summary>Recalcula status agregando parcelas. Chamado apos cada pagamento ou job vencimento.</summary>
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

    /// <summary>
    /// Status para EXIBICAO em listagens/badges: deriva Vencida por data (parcela nao
    /// paga/cancelada com vencimento &lt; hoje) SEM persistir — rede sobre o job diario de
    /// vencimento, pro badge ficar correto na janela ate o job rodar (BUG-022, decisao
    /// "ambos"). Exige Parcelas carregadas; sem elas degrada pro Status armazenado.
    /// </summary>
    public StatusContaFinanceira StatusEfetivo(DateTime hojeUtc)
    {
        if (Status != StatusContaFinanceira.Aberta && Status != StatusContaFinanceira.ParcialmentePaga)
            return Status;
        var temVencida = Parcelas.Any(p =>
            p.Status != StatusParcela.Paga &&
            p.Status != StatusParcela.Cancelada &&
            p.DataVencimento.Date < hojeUtc.Date);
        return temVencida ? StatusContaFinanceira.Vencida : Status;
    }

    public decimal TotalPago => Parcelas.Where(p => p.Status != StatusParcela.Cancelada).Sum(p => p.ValorPago);
    public decimal Pendente
    {
        get
        {
            var saldo = ValorTotal - TotalPago;
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

/// <summary>
/// Audit por campo de alteracao em <see cref="ContaPagar"/>. Padrao identico
/// ao <see cref="ClienteAlteracao"/>.
/// </summary>
public class ContaPagarAlteracao
{
    public Guid Id { get; set; }
    public Guid ContaPagarId { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid? AlteradoPorUserId { get; set; }
    public string? AlteradoPorNome { get; set; }
    public string Campo { get; set; } = null!;
    public string? ValorAntigo { get; set; }
    public string? ValorNovo { get; set; }
    public DateTime AlteradoEm { get; set; }
    public string? Origem { get; set; } // "web" | "mobile" | "api" | "job" | "webhook"

    public ContaPagar? ContaPagar { get; set; }
}
