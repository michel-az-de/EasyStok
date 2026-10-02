namespace EasyStock.Application.UseCases.ResetarSenha;

/// <summary>Reset pelo link do e-mail. <paramref name="Ip"/> e <paramref name="UserAgent"/> vêm da conexão (controller), nunca do corpo.</summary>
public sealed record ResetarSenhaCommand(string Token, string NovaSenha, string? Ip = null, string? UserAgent = null) : ICommand;

/// <summary>Reset pelo código de 6 dígitos do WhatsApp (N8). Nunca serve para superadmin.</summary>
public sealed record ResetarSenhaPorCodigoCommand(
    string Email, string Codigo, string NovaSenha, string? Ip = null, string? UserAgent = null) : ICommand;

public sealed record ResetarSenhaResult(bool Success);
