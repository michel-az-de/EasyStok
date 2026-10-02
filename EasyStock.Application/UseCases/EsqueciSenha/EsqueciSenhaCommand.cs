namespace EasyStock.Application.UseCases.EsqueciSenha;

/// <summary>
/// O pedido de redefinição (N8). <paramref name="Ip"/> e <paramref name="UserAgent"/> vêm da conexão, montados pelo
/// controller: nunca do corpo da requisição. Não há <c>BaseUrl</c>: a base do link vem da configuração.
/// </summary>
public sealed record EsqueciSenhaCommand(string Email, string? Ip = null, string? UserAgent = null) : ICommand;

public sealed record EsqueciSenhaResult(bool Success);