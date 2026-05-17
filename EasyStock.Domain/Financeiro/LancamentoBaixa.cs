namespace EasyStock.Domain.Financeiro;

/// <summary>
/// Evento de baixa (pagamento total ou parcial) de um <see cref="Lancamento"/>.
/// Entidade filha do agregado Lancamento — nao deve ser construida fora do
/// metodo <see cref="Lancamento.RegistrarBaixa"/>, que valida invariantes
/// (valor restante, status, idempotencia por <see cref="ChaveExterna"/>).
///
/// <para>
/// Tabela append-only: estorno gera nova linha com sinal contrario via
/// <c>Estornar()</c>, preservando a baixa original para audit trail (ECD/ITG 2000).
/// </para>
/// </summary>
public sealed class LancamentoBaixa
{
    public Guid Id { get; private set; }
    public Guid LancamentoId { get; private set; }
    public Guid EmpresaId { get; private set; }
    public decimal Valor { get; private set; }
    public DateTime DataBaixa { get; private set; }

    /// <summary>FK opcional para futura entidade ContaBancaria. String enquanto
    /// nao existe a tabela — campo guardado para nao quebrar contratos quando
    /// a entidade for criada.</summary>
    public Guid? ContaBancariaId { get; private set; }

    /// <summary>"pix" | "dinheiro" | "credito" | "debito" | "transferencia" | "boleto" | "outro".</summary>
    public string MeioPagamento { get; private set; } = "outro";

    /// <summary>Identificador externo do pagamento (txid PIX, NSU, n° comprovante).
    /// Usado como chave de idempotencia: duas baixas com mesma chave (na mesma
    /// empresa+lancamento) sao a mesma operacao.</summary>
    public string? ChaveExterna { get; private set; }

    public string? Observacao { get; private set; }
    public Guid? RegistradoPorUserId { get; private set; }
    public string? RegistradoPorNome { get; private set; }
    public DateTime CriadoEm { get; private set; }

    /// <summary>Marca de estorno. Quando preenchido, a baixa nao conta mais
    /// para o valor quitado do lancamento.</summary>
    public DateTime? EstornadoEm { get; private set; }
    public string? MotivoEstorno { get; private set; }

    public bool Ativa => EstornadoEm == null;

    private LancamentoBaixa() { }

    internal static LancamentoBaixa Criar(
        Guid empresaId,
        Guid lancamentoId,
        decimal valor,
        DateTime dataBaixa,
        string meioPagamento,
        Guid? contaBancariaId,
        string? chaveExterna,
        string? observacao,
        Guid? registradoPorUserId,
        string? registradoPorNome)
    {
        return new LancamentoBaixa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            LancamentoId = lancamentoId,
            Valor = decimal.Round(valor, 2, MidpointRounding.ToEven),
            DataBaixa = dataBaixa,
            MeioPagamento = string.IsNullOrWhiteSpace(meioPagamento) ? "outro" : meioPagamento.Trim().ToLowerInvariant(),
            ContaBancariaId = contaBancariaId,
            ChaveExterna = string.IsNullOrWhiteSpace(chaveExterna) ? null : chaveExterna.Trim(),
            Observacao = observacao,
            RegistradoPorUserId = registradoPorUserId,
            RegistradoPorNome = registradoPorNome,
            CriadoEm = DateTime.UtcNow
        };
    }

    internal void Estornar(string? motivo)
    {
        if (EstornadoEm != null) return;
        EstornadoEm = DateTime.UtcNow;
        MotivoEstorno = motivo;
    }
}
