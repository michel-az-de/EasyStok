using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.CriarSugestaoCompra;

public sealed record CriarSugestaoCompraCommand(
    Guid EmpresaId,
    Guid? LojaId,
    IReadOnlyList<FornecedorGrupoInput> Fornecedores,
    string? Canal,
    string? Observacoes,
    string IdempotencyKey);

public sealed record FornecedorGrupoInput(
    Guid FornecedorId,
    IReadOnlyList<ItemFaltanteInput> Itens);

public sealed record ItemFaltanteInput(
    Guid InsumoId,
    string Nome,
    decimal Quantidade,
    UnidadeMedida Unidade,
    decimal CustoUnitario,
    string? Observacao);
