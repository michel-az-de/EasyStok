namespace EasyStock.Api.Mobile.DTOs;

/// <summary>#2813 (B4): totais do fechamento que veio no snapshot do aparelho. Valores em REAIS, como no aparelho (nao e centavo).</summary>
public record LegacyCashClosingTotalsDto(decimal Sold, decimal In, decimal Out, decimal Balance,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)] decimal? ConferredBalance,
    int EntriesCount, int OrdersCount);

/// <summary>#2813 (B4): fechamento de caixa do aparelho, somente leitura. ClosedAt em UTC (vem de closedAt em ms); DateKey e "YYYY-MM-DD".</summary>
public record LegacyCashClosingDto(string Id, string DateKey, DateTime ClosedAt, string ClosedBy,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)] string? Notes,
    LegacyCashClosingTotalsDto Totals, string Origem, Guid ImportId);

/// <summary>Itens ordenados por DateKey desc e ClosedAt desc. Ignorados conta os itens pulados por campo ausente ou de tipo errado.</summary>
public record LegacyCashClosingsResponse(List<LegacyCashClosingDto> Itens, int Ignorados);
