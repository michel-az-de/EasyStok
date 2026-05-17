using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.GerenciarComposicao;

public sealed record ComposicaoResult(
    Guid ProdutoFinalId,
    string ProdutoFinalNome,
    Guid? LojaId,
    decimal RendimentoBase,
    UnidadeMedida RendimentoUnidade,
    UnidadeMedida UnidadeMedidaBase,
    IReadOnlyList<ComposicaoLinhaResult> Linhas);

public sealed record ComposicaoLinhaResult(
    Guid InsumoId,
    string InsumoNome,
    decimal Quantidade,
    UnidadeMedida Unidade,
    string? Observacao,
    int OrdemExibicao);
