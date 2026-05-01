using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.ConfirmEmail;

public sealed record ConfirmEmailCommand(string Token) : ICommand;
