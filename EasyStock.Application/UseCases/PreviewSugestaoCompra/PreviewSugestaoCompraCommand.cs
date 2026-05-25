using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.PreviewSugestaoCompra;

public sealed record PreviewSugestaoCompraCommand(
    Guid EmpresaId,
    Guid? LojaId,
    IReadOnlyList<InsumoFaltanteInput> Insumos);

public sealed record InsumoFaltanteInput(
    Guid InsumoId,
    decimal QuantidadeFaltante,
    UnidadeMedida Unidade);
