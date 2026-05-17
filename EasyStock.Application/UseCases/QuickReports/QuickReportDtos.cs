namespace EasyStock.Application.UseCases.QuickReports;

// ─── DTOs dos Quick Reports (§27.7) ─────────────────────────────────────────

/// <summary>DTO de resposta do quick report vendas-hoje.</summary>
public sealed record VendasHojeDto(
    decimal Total,
    int     QtdVendas,
    decimal TicketMedio,
    IReadOnlyList<TopProdutoDto> TopProdutos);

/// <summary>Item de top-5 produtos do dia (vendas-hoje).</summary>
public sealed record TopProdutoDto(
    Guid    ProdutoId,
    string  Nome,
    decimal Qtd);

/// <summary>DTO de resposta do quick report caixa-turno.</summary>
public sealed record CaixaTurnoDto(
    decimal  TotalEntradas,
    decimal  TotalSaidas,
    decimal  TotalVendas,
    decimal  SaldoAtual,
    string?  Operador);

/// <summary>DTO de resposta do quick report estoque-busca.</summary>
public sealed record EstoqueBuscaDto(
    Guid     ItemEstoqueId,
    string   Sku,
    string   Nome,
    string?  Variacao,
    string?  LojaNome,
    decimal  QtdAtual,
    decimal  CustoUnitario,
    decimal  ValorEstoque,
    string   StatusEstoque);

/// <summary>DTO de resposta do quick report nfce-hoje.</summary>
public sealed record NfceHojeDto(
    int     Autorizadas,
    int     Canceladas,
    int     Rejeitadas,
    int     Pendentes,
    decimal PercentSucesso);

/// <summary>Item de ranking de vendas por vendedor (vendas-vendedor-turno).</summary>
public sealed record VendedorTurnoDto(
    Guid?   VendedorId,
    string  VendedorNome,
    int     QtdVendas,
    decimal TotalVendido,
    int     Ranking);

/// <summary>DTO de resposta do quick report vendas-vendedor-turno.</summary>
public sealed record VendasVendedorTurnoDto(
    IReadOnlyList<VendedorTurnoDto> Vendedores,
    decimal TotalGeral,
    int     QtdVendasGeral);
