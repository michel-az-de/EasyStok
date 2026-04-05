using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.EsqueciSenha;

public sealed record EsqueciSenhaCommand(string Email) : ICommand;

public sealed record EsqueciSenhaResult(bool Success);