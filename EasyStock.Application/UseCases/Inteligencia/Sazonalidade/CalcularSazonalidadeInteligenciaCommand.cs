namespace EasyStock.Application.UseCases.Inteligencia.Sazonalidade;

public sealed record CalcularSazonalidadeInteligenciaCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid ProdutoId,
    [property: Range(1, 36)] int Meses = 12);
