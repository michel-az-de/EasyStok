namespace EasyStock.Application.UseCases.Analytics.Sazonalidade;

public sealed record CalcularSazonalidadeCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid ProdutoId,
    [property: Range(1, 36)] int Meses = 12,
    Guid? LojaId = null);
