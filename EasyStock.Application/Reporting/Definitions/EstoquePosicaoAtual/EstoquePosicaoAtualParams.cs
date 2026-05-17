namespace EasyStock.Application.Reporting.Definitions.EstoquePosicaoAtual;

/// <summary>
/// Parâmetros para o relatório de Posição de Estoque Atual.
/// </summary>
public sealed record EstoquePosicaoAtualParams(
    Guid?  LojaId             = null,
    Guid?  CategoriaId        = null,
    bool   IncluirSemEstoque  = false);
