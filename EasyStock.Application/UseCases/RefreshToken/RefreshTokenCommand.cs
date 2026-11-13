namespace EasyStock.Application.UseCases.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshToken, Guid? RequisicaoId = null) : ICommand;

public sealed record RefreshTokenResult(string AccessToken, string RefreshToken, int ExpiresIn);
