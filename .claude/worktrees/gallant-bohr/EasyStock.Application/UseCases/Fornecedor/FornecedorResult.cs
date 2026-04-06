namespace EasyStock.Application.UseCases.Fornecedor;

public sealed record FornecedorResult(
    Guid Id,
    Guid EmpresaId,
    string Nome,
    bool Ativo,
    string? Documento = null,
    string? Email = null,
    string? Telefone = null,
    string? Contato = null,
    string? Categoria = null,
    string? Tipo = null,
    int? LeadTimeEstimadoDias = null,
    decimal? LeadTimeRealMedioDias = null,
    string? SiteUrl = null,
    string? PedidoMinimo = null,
    string? FretePadrao = null,
    string? Observacoes = null);
