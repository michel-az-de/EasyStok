namespace EasyStock.Application.UseCases.Analytics.PedidosPorAnuncio;

public sealed record ObterPedidosPorAnuncioCommand(
    [property: Required] Guid EmpresaId,
    DateTime? DataDe = null,
    DateTime? DataAte = null,
    [property: Range(1, 365)] int DiasPadrao = 30);
