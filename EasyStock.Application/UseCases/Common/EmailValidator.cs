using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Helper de validação de endereços de email usado pelos use cases.
/// Delegates to <see cref="EmailAddress"/> value object — fonte única de verdade para
/// regras de formato de e-mail. Mantém a superfície de exceções específica da camada
/// Application (<see cref="UseCaseValidationException"/>, mapeada para HTTP 400).
/// </summary>
public static class EmailValidator
{
    /// <summary>Retorna true se <paramref name="email"/> está num formato válido.</summary>
    public static bool IsValid(string? email) => EmailAddress.TryFrom(email) is not null;

    /// <summary>Lança <see cref="UseCaseValidationException"/> se o email não for válido.</summary>
    public static void EnsureValid(string? email, string campoAmigavel = "Email")
    {
        if (!IsValid(email))
            throw new UseCaseValidationException($"{campoAmigavel} inválido.");
    }
}
