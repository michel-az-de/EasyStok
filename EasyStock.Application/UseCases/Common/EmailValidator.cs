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

    /// <summary>#2695: e-mail de pessoa da equipe. Formato válido e fora do domínio das contas internas.</summary>
    public static void EnsureValidDeUsuario(string? email)
    {
        EnsureValid(email);
        RecusarReservado(email);
    }

    /// <summary>#2695: o domínio das contas internas não serve para pessoa (o domínio também barra, ver <c>Usuario</c>).</summary>
    public static void RecusarReservado(string? email)
    {
        if (email is not null && Usuario.EmailReservado(email))
            throw new UseCaseValidationException(
                "EMAIL_RESERVADO", "Este domínio de e-mail é reservado ao sistema. Use o e-mail da pessoa.");
    }
}
