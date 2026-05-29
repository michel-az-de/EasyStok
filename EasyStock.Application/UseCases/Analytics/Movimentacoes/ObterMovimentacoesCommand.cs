namespace EasyStock.Application.UseCases.Analytics.Movimentacoes;

public sealed record ObterMovimentacoesCommand(
    [property: Required] Guid EmpresaId,
    DateTime? DataDe = null,
    DateTime? DataAte = null,
    TipoMovimentacaoEstoque? Tipo = null,
    [property: Range(1, 365)] int DiasPadrao = 30,
    Guid? LojaId = null);
