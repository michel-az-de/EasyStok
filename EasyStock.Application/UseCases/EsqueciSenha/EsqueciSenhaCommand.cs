namespace EasyStock.Application.UseCases.EsqueciSenha;

public sealed record EsqueciSenhaCommand(string Email, string? BaseUrl = null) : ICommand;

public sealed record EsqueciSenhaResult(bool Success);