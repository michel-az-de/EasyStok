namespace EasyStock.Application.UseCases.Fornecedor;

public sealed record FornecedorResult(Guid Id, Guid EmpresaId, string Nome, bool Ativo);
