using EasyStock.Application.UseCases.Logout;
using FluentValidation;

namespace EasyStock.Application.Validators;

public class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token é obrigatório.");
    }
}
