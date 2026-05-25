using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Logout;

public sealed record LogoutCommand(string RefreshToken) : ICommand;

public sealed record LogoutResult(bool Success);
