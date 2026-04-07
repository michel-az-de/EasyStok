using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.ResetarSenha;

public sealed record ResetarSenhaCommand(string Token, string NovaSenha) : ICommand;

public sealed record ResetarSenhaResult(bool Success);