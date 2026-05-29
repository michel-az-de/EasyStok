namespace EasyStock.Application.UseCases.AlterarSenha;

public sealed record AlterarSenhaCommand(string SenhaAtual, string NovaSenha) : ICommand;

public sealed record AlterarSenhaResult(bool Success);