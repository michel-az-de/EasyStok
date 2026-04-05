namespace EasyStock.Application.UseCases.Loja;

public sealed record LojaResult(Guid Id, Guid EmpresaId, string Nome, bool Ativa);
