namespace EasyStock.Application.UseCases.GerenciarComposicao;

public sealed record ObterComposicaoQuery(
    Guid EmpresaId,
    Guid ProdutoFinalId,
    Guid? LojaId);
