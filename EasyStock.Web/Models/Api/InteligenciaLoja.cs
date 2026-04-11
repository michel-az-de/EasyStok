namespace EasyStock.Web.Models.Api;

public record LojaComparacaoApi
{
    public Guid LojaId { get; init; }
    public string NomeLoja { get; init; } = "";
    public decimal HealthScore { get; init; }
    public string HealthClassificacao { get; init; } = "";
    public decimal ReceitaPeriodo { get; init; }
    public int TotalSkus { get; init; }
    public int QuantidadeEstoque { get; init; }
    public decimal ValorEstoque { get; init; }
    public int AlertasTotal { get; init; }
    public int AlertasCriticos { get; init; }
    public int AlertasVencimento { get; init; }
    public int ItensParados { get; init; }
    public int ItensAbaixoMinimo { get; init; }
    public decimal MediaVendasDiaria { get; init; }
}

public record LojaResumoInteligenciaApi
{
    public Guid LojaId { get; init; }
    public string NomeLoja { get; init; } = "";
    public int TotalSkus { get; init; }
    public int QuantidadeTotalEmEstoque { get; init; }
    public decimal ValorTotalEstoque { get; init; }
    public decimal ValorCustoEstoque { get; init; }
    public int AlertasEstoqueBaixo { get; init; }
    public int AlertasVencimento { get; init; }
    public int AlertasItensParados { get; init; }
    public int ItensAbaixoMinimo { get; init; }
    public decimal MediaVendasDiaria { get; init; }
    public decimal ReceitaPeriodo { get; init; }
    public DateTime? UltimaMovimentacao { get; init; }
    public decimal HealthScore { get; init; }
    public string HealthClassificacao { get; init; } = "";
    public decimal DimStockHealth { get; init; }
    public decimal DimSalesVelocity { get; init; }
    public decimal DimExpiryRisk { get; init; }
    public decimal DimIdleRisk { get; init; }
    public decimal DimReplenishmentUrgency { get; init; }
}

public record ProdutoTurnoverApi
{
    public Guid ProdutoId { get; init; }
    public string NomeProduto { get; init; } = "";
    public int QuantidadeVendida { get; init; }
    public decimal ReceitaGerada { get; init; }
    public decimal TaxaSaidaDiaria { get; init; }
}

public record IndicadorAcaoApi
{
    public string Tipo { get; init; } = "";
    public string Severidade { get; init; } = "";
    public string Titulo { get; init; } = "";
    public string Descricao { get; init; } = "";
    public Guid? LojaId { get; init; }
    public string? NomeLoja { get; init; }
    public Guid? ReferenciaId { get; init; }
}
