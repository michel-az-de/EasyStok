using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Financeiro.Events;

namespace EasyStock.Domain.Financeiro;

/// <summary>
/// Agregado raiz do modulo Financeiro. Representa um movimento previsto/realizado
/// (Conta a Receber ou Conta a Pagar). Diferente de <see cref="Fatura"/> (cobranca
/// SaaS) e <see cref="MovimentoCaixa"/> (caixa fisico): Lancamento e o titulo a
/// receber/pagar do tenant — modela o lado AR/AP do ERP.
///
/// <para>
/// Ciclo: <see cref="StatusLancamento.Pendente"/> -> <see cref="StatusLancamento.Parcial"/>
/// -> <see cref="StatusLancamento.Quitado"/>. Pode ir direto a Cancelado a partir
/// de Pendente ou Parcial. Quitado e Cancelado sao terminais.
/// </para>
///
/// <para>
/// Baixas (pagamentos) sao registradas via <see cref="RegistrarBaixa"/> que valida
/// invariantes (valor restante, status, idempotencia via ChaveExterna) e gera
/// um <see cref="LancamentoBaixadoEvent"/> em <see cref="EventosPendentes"/>.
/// </para>
/// </summary>
public sealed class Lancamento
{
    private readonly List<LancamentoBaixa> _baixas = new();
    private readonly List<LancamentoBaixadoEvent> _eventos = new();

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public TipoLancamento Tipo { get; private set; }
    public string Descricao { get; private set; } = null!;
    public decimal Valor { get; private set; }
    public DateTime DataEmissao { get; private set; }
    public DateTime DataVencimento { get; private set; }
    public StatusLancamento Status { get; private set; } = StatusLancamento.Pendente;

    public Guid? ClienteId { get; private set; }
    public Guid? FornecedorId { get; private set; }

    /// <summary>Categoria livre (free-text enquanto nao existe a entidade
    /// <c>CategoriaFinanceira</c>). Ex: "matera-prima", "aluguel", "venda balcao".</summary>
    public string? Categoria { get; private set; }

    /// <summary>Identificador externo do titulo (NF, contrato, n° boleto). Distinto
    /// de <see cref="LancamentoBaixa.ChaveExterna"/> que identifica o pagamento.</summary>
    public string? DocumentoReferencia { get; private set; }

    public string? Observacoes { get; private set; }
    public DateTime? CanceladoEm { get; private set; }
    public string? MotivoCancelamento { get; private set; }

    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    /// <summary>Optimistic concurrency via xmin (Postgres system column). Mapeada
    /// como rowversion. Defesa redundante ao FOR UPDATE em baixas concorrentes.</summary>
    public uint Versao { get; set; }

    public IReadOnlyCollection<LancamentoBaixa> Baixas => _baixas.AsReadOnly();
    public IReadOnlyCollection<LancamentoBaixadoEvent> EventosPendentes => _eventos.AsReadOnly();

    /// <summary>Total efetivamente baixado (excluindo baixas estornadas).</summary>
    public decimal TotalBaixado => _baixas.Where(b => b.Ativa).Sum(b => b.Valor);

    /// <summary>Saldo a baixar. Zero quando quitado.</summary>
    public decimal ValorRestante
    {
        get
        {
            var saldo = Valor - TotalBaixado;
            return saldo < 0m ? 0m : decimal.Round(saldo, 2, MidpointRounding.ToEven);
        }
    }

    private Lancamento() { }

    public static Lancamento Criar(
        Guid empresaId,
        TipoLancamento tipo,
        string descricao,
        decimal valor,
        DateTime dataEmissao,
        DateTime dataVencimento,
        Guid? clienteId = null,
        Guid? fornecedorId = null,
        string? categoria = null,
        string? documentoReferencia = null,
        string? observacoes = null)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId obrigatorio.");
        if (string.IsNullOrWhiteSpace(descricao))
            throw new RegraDeDominioVioladaException("Descricao obrigatoria.");
        if (valor <= 0m)
            throw new RegraDeDominioVioladaException("Valor do lancamento deve ser maior que zero.");
        if (dataVencimento.Date < dataEmissao.Date)
            throw new RegraDeDominioVioladaException("Vencimento nao pode ser anterior a emissao.");

        var agora = DateTime.UtcNow;
        return new Lancamento
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Tipo = tipo,
            Descricao = descricao.Trim(),
            Valor = decimal.Round(valor, 2, MidpointRounding.ToEven),
            DataEmissao = dataEmissao,
            DataVencimento = dataVencimento,
            Status = StatusLancamento.Pendente,
            ClienteId = clienteId,
            FornecedorId = fornecedorId,
            Categoria = string.IsNullOrWhiteSpace(categoria) ? null : categoria.Trim(),
            DocumentoReferencia = string.IsNullOrWhiteSpace(documentoReferencia) ? null : documentoReferencia.Trim(),
            Observacoes = observacoes,
            CriadoEm = agora,
            AlteradoEm = agora
        };
    }

    /// <summary>
    /// Registra uma baixa (pagamento) total ou parcial. Retorna a baixa criada,
    /// ou a baixa preexistente quando <paramref name="chaveExterna"/> ja foi usada
    /// para este lancamento (idempotencia). Atualiza <see cref="Status"/> e enfileira
    /// um <see cref="LancamentoBaixadoEvent"/> em <see cref="EventosPendentes"/>.
    ///
    /// <para>Invariantes: nao baixa lancamento Cancelado; valor &gt; 0; nao excede
    /// <see cref="ValorRestante"/>.</para>
    /// </summary>
    public LancamentoBaixa RegistrarBaixa(
        decimal valor,
        DateTime dataBaixa,
        string meioPagamento,
        Guid? contaBancariaId = null,
        string? chaveExterna = null,
        string? observacao = null,
        Guid? registradoPorUserId = null,
        string? registradoPorNome = null)
    {
        if (Status == StatusLancamento.Cancelado)
            throw new RegraDeDominioVioladaException("Lancamento cancelado nao admite baixa.");
        if (Status == StatusLancamento.Quitado)
            throw new RegraDeDominioVioladaException("Lancamento ja quitado.");
        if (valor <= 0m)
            throw new RegraDeDominioVioladaException("Valor da baixa deve ser maior que zero.");

        if (!string.IsNullOrWhiteSpace(chaveExterna))
        {
            var chave = chaveExterna.Trim();
            var existente = _baixas.FirstOrDefault(b => b.Ativa && b.ChaveExterna == chave);
            if (existente != null) return existente;
        }

        var valorArredondado = decimal.Round(valor, 2, MidpointRounding.ToEven);
        if (valorArredondado > ValorRestante)
            throw new RegraDeDominioVioladaException(
                $"Baixa de {valorArredondado:F2} excede o saldo restante ({ValorRestante:F2}).");

        var baixa = LancamentoBaixa.Criar(
            EmpresaId,
            Id,
            valorArredondado,
            dataBaixa,
            meioPagamento,
            contaBancariaId,
            chaveExterna,
            observacao,
            registradoPorUserId,
            registradoPorNome);

        _baixas.Add(baixa);
        AtualizarStatusPosBaixa();
        AlteradoEm = DateTime.UtcNow;

        _eventos.Add(new LancamentoBaixadoEvent(
            EventoId: Guid.NewGuid(),
            OcorridoEm: AlteradoEm,
            EmpresaId: EmpresaId,
            LancamentoId: Id,
            BaixaId: baixa.Id,
            ValorBaixado: baixa.Valor,
            ValorRestante: ValorRestante,
            StatusResultante: Status,
            MeioPagamento: baixa.MeioPagamento,
            ContaBancariaId: baixa.ContaBancariaId,
            ChaveExterna: baixa.ChaveExterna));

        return baixa;
    }

    /// <summary>
    /// Estorna uma baixa previamente registrada. Recalcula o status. Idempotente
    /// se a baixa ja estiver estornada.
    /// </summary>
    public void EstornarBaixa(Guid baixaId, string? motivo = null)
    {
        var baixa = _baixas.FirstOrDefault(b => b.Id == baixaId)
            ?? throw new RegraDeDominioVioladaException("Baixa nao encontrada.");

        if (!baixa.Ativa) return;
        baixa.Estornar(motivo);
        AtualizarStatusPosBaixa();
        AlteradoEm = DateTime.UtcNow;
    }

    public void Cancelar(string? motivo = null)
    {
        if (Status == StatusLancamento.Cancelado) return;
        if (Status == StatusLancamento.Quitado)
            throw new RegraDeDominioVioladaException("Lancamento quitado nao pode ser cancelado.");
        if (TotalBaixado > 0m)
            throw new RegraDeDominioVioladaException("Estorne as baixas antes de cancelar o lancamento.");

        Status = StatusLancamento.Cancelado;
        CanceladoEm = DateTime.UtcNow;
        MotivoCancelamento = motivo;
        AlteradoEm = CanceladoEm.Value;
    }

    /// <summary>Limpa a fila de eventos apos publicacao pelo UseCase. NAO chamar
    /// antes do commit transacional — eventos nao publicados sao perdidos.</summary>
    public void LimparEventosPendentes() => _eventos.Clear();

    private void AtualizarStatusPosBaixa()
    {
        if (Status == StatusLancamento.Cancelado) return;

        var total = TotalBaixado;
        if (total <= 0m) Status = StatusLancamento.Pendente;
        else if (total >= Valor) Status = StatusLancamento.Quitado;
        else Status = StatusLancamento.Parcial;
    }
}
