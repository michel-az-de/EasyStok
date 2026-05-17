using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.GerenciarComposicao;

public sealed record SubstituirComposicaoCommand(
    Guid EmpresaId,
    Guid ProdutoFinalId,
    Guid? LojaId,
    Guid UsuarioId,
    decimal RendimentoBase,
    UnidadeMedida RendimentoUnidade,
    UnidadeMedida UnidadeMedidaBaseProdutoFinal,
    IReadOnlyList<ComposicaoLinhaInput> Linhas,
    string? Observacao);

public sealed record ComposicaoLinhaInput(
    Guid InsumoId,
    decimal Quantidade,
    UnidadeMedida Unidade,
    string? Observacao,
    int OrdemExibicao);
