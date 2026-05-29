namespace EasyStock.Application.UseCases.Inteligencia.SugestaoReposicao;

public sealed record ObterSugestaoReposicaoCommand(
    [property: Required] Guid EmpresaId,
    Guid? LojaId = null,
    int? LimiteQuantidade = null,
    [property: Range(1, int.MaxValue)] int Page = 1,
    [property: Range(1, 100)] int PageSize = 20);
