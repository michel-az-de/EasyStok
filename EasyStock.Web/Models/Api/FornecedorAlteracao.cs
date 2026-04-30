namespace EasyStock.Web.Models.Api;

/// <summary>Onda P4 — entrada do trail de alterações de fornecedor.</summary>
public record FornecedorAlteracaoDto(
    string Id,
    string FornecedorId,
    Guid? AlteradoPorUserId,
    string? AlteradoPorNome,
    string Campo,
    string? ValorAntigo,
    string? ValorNovo,
    DateTime AlteradoEm,
    string? Origem
);
