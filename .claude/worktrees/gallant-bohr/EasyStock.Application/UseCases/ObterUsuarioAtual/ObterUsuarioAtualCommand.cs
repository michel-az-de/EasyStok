using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.ObterUsuarioAtual;

public sealed record ObterUsuarioAtualCommand : ICommand;

public sealed record ObterUsuarioAtualResult(Guid Id, string Nome, string Email, bool Ativo, DateTime CriadoEm);