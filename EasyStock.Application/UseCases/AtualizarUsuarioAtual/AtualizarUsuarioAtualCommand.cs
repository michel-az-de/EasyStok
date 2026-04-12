using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.AtualizarUsuarioAtual;

public sealed record AtualizarUsuarioAtualCommand(string? Nome, string? Email, string? TemaPreferido = null) : ICommand;

public sealed record AtualizarUsuarioAtualResult(Guid Id, string Nome, string Email, string TemaPreferido);
