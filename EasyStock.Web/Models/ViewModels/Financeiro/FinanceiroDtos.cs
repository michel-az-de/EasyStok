namespace EasyStock.Web.Models.ViewModels.Financeiro;

public class CategoriaFinanceiraApi
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";
    public string Tipo { get; set; } = "";
    public Guid? ParentId { get; set; }
    public int Profundidade { get; set; }
    public bool Ativa { get; set; }
    public string? Cor { get; set; }
    public string? Icone { get; set; }
}

public class CentroCustoApi
{
    public Guid Id { get; set; }
    public Guid? LojaId { get; set; }
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public string? Descricao { get; set; }
    public bool Ativo { get; set; }
}

public class ParcelaApi
{
    public Guid Id { get; set; }
    public int Numero { get; set; }
    public decimal Valor { get; set; }
    public decimal ValorPago { get; set; }
    public decimal Saldo { get; set; }
    public DateTime DataVencimento { get; set; }
    public DateTime? DataPagamentoTotal { get; set; }
    public string Status { get; set; } = "";
    public string? MetodoPlanejado { get; set; }
    public string? EfiTxid { get; set; }
    public string? PixCopiaCola { get; set; }
    public string? QrCodeBase64 { get; set; }
    public DateTime? PixExpiraEm { get; set; }
    public List<PagamentoParcelaApi> Pagamentos { get; set; } = [];
}

public class PagamentoParcelaApi
{
    public Guid Id { get; set; }
    public string Lado { get; set; } = "";
    public decimal Valor { get; set; }
    public string Metodo { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime DataPagamento { get; set; }
    public string? GatewayProvedor { get; set; }
    public string? GatewayTransactionId { get; set; }
    public Guid? MovimentoCaixaId { get; set; }
    public DateTime? EstornadoEm { get; set; }
    public string? MotivoEstorno { get; set; }
    public string? Observacao { get; set; }
}

/// <summary>Contexto para o partial _PagamentosParcela renderizar o histórico + o botão de estornar.</summary>
public record PagamentosParcelaVm(string RotaBase, Guid ContaId, Guid ParcelaId, List<PagamentoParcelaApi> Pagamentos);

public class ContaPagarApi
{
    public Guid Id { get; set; }
    public Guid? FornecedorId { get; set; }
    public Guid CategoriaFinanceiraId { get; set; }
    public Guid? CentroCustoId { get; set; }
    public string Descricao { get; set; } = "";
    public string? Observacoes { get; set; }
    public decimal ValorTotal { get; set; }
    public decimal TotalPago { get; set; }
    public decimal Pendente { get; set; }
    public string Status { get; set; } = "";
    public DateTime DataEmissao { get; set; }
    public DateTime? DataCompetencia { get; set; }
    public string Origem { get; set; } = "";
    public Guid? OrigemRefId { get; set; }
    public string? DocumentoReferencia { get; set; }
    public DateTime? CanceladaEm { get; set; }
    public string? MotivoCancelamento { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }
    public List<ParcelaApi> Parcelas { get; set; } = [];
}

public class ContaReceberApi
{
    public Guid Id { get; set; }
    public Guid? ClienteId { get; set; }
    public Guid CategoriaFinanceiraId { get; set; }
    public Guid? CentroCustoId { get; set; }
    public string Descricao { get; set; } = "";
    public string? Observacoes { get; set; }
    public decimal ValorTotal { get; set; }
    public decimal TotalRecebido { get; set; }
    public decimal Pendente { get; set; }
    public string Status { get; set; } = "";
    public DateTime DataEmissao { get; set; }
    public DateTime? DataCompetencia { get; set; }
    public string Origem { get; set; } = "";
    public Guid? OrigemRefId { get; set; }
    public string? DocumentoReferencia { get; set; }
    public DateTime? CanceladaEm { get; set; }
    public string? MotivoCancelamento { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }
    public List<ParcelaApi> Parcelas { get; set; } = [];
}

public class ContasPagarPaginadas
{
    public List<ContaPagarApi> Itens { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class ContasReceberPaginadas
{
    public List<ContaReceberApi> Itens { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class ContasPagarIndexViewModel
{
    public ContasPagarPaginadas? Resultado { get; set; }
    public string? FiltroStatus { get; set; }
    public string? Busca { get; set; }
    public DateTime? VencimentoDe { get; set; }
    public DateTime? VencimentoAte { get; set; }
    public List<CategoriaFinanceiraApi> Categorias { get; set; } = [];
    public List<CentroCustoApi> CentrosCusto { get; set; } = [];
}

public class ContasReceberIndexViewModel
{
    public ContasReceberPaginadas? Resultado { get; set; }
    public string? FiltroStatus { get; set; }
    public string? Busca { get; set; }
    public DateTime? VencimentoDe { get; set; }
    public DateTime? VencimentoAte { get; set; }
    public List<CategoriaFinanceiraApi> Categorias { get; set; } = [];
    public List<CentroCustoApi> CentrosCusto { get; set; } = [];
}

public class CriarContaViewModel
{
    public List<CategoriaFinanceiraApi> Categorias { get; set; } = [];
    public List<CentroCustoApi> CentrosCusto { get; set; } = [];

    // Valores postados — repopulados no re-render apos erro (W-005/6: preserva o que foi digitado).
    public string? Descricao { get; set; }
    public Guid? CategoriaFinanceiraId { get; set; }
    public Guid? CentroCustoId { get; set; }
    public DateTime? DataEmissao { get; set; }
    public decimal? ValorTotal { get; set; }
    public int? NumeroParcelas { get; set; }
    public DateTime? PrimeiraVencimento { get; set; }
    public string? IntervaloTipo { get; set; }
    public string? Observacoes { get; set; }
    public bool EmitirAposCriar { get; set; }
    public string? Erro { get; set; }
}

public class ContaPagarDetalheViewModel
{
    public ContaPagarApi Conta { get; set; } = new();
}

public class ContaReceberDetalheViewModel
{
    public ContaReceberApi Conta { get; set; } = new();
}

/// <summary>
/// Deltas e series temporais derivados do fluxo de caixa, para alimentar os
/// &lt;es-stat-card&gt; do dashboard financeiro (delta %, delta-trend, sparkline).
/// Calculado 100% no Web a partir de <c>ObterFluxoCaixaAsync</c> — sem migrar o
/// DTO de dominio (DashboardFinanceiroDto permanece escalar).
///
/// Convencao dos campos *Delta: string ja formatada pt-BR pronta pro atributo
/// <c>delta=</c> ("+12,5%", "-3,2%", "0%") ou <c>null</c> quando nao ha base de
/// comparacao (periodo anterior == 0). *Trend e "up"|"down"|"flat" pro atributo
/// <c>delta-trend=</c>. *Serie e CSV InvariantCulture pronto pro atributo
/// <c>sparkline=</c> (o EsStatCardTagHelper aceita CSV).
/// </summary>
public class DashboardDeltasApi
{
    // A Receber 30d (base: PrevistoReceber).
    public string? ReceberDelta { get; set; }
    public string ReceberTrend { get; set; } = "flat";
    public string? ReceberSerie { get; set; }

    // A Pagar 30d (base: PrevistoPagar).
    public string? PagarDelta { get; set; }
    public string PagarTrend { get; set; } = "flat";
    public string? PagarSerie { get; set; }

    // Saldo do mes (base: RealizadoReceber - RealizadoPagar).
    public string? SaldoDelta { get; set; }
    public string SaldoTrend { get; set; } = "flat";
    public string? SaldoSerie { get; set; }
}

/// <summary>
/// View model do dashboard Financeiro (/financeiro). Empacota os KPIs escalares
/// (<see cref="DashboardFinanceiroApi"/>) e os deltas/series derivados
/// (<see cref="DashboardDeltasApi"/>) sem acoplar um ao outro.
/// </summary>
public class FinanceiroIndexViewModel
{
    public EasyStock.Web.Services.DashboardFinanceiroApi Dashboard { get; set; } = new();
    public DashboardDeltasApi Deltas { get; set; } = new();
}
