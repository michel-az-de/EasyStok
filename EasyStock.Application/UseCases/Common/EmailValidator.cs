using System.Net.Mail;

namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Helper de validação de endereços de email usado pelos use cases.
/// Usa <see cref="MailAddress"/> do framework para reaproveitar a mesma
/// sintaxe aceita pelo RFC-5322 (bem mais permissivo que um regex manual).
/// </summary>
public static class EmailValidator
{
    /// <summary>Retorna true se <paramref name="email"/> está num formato de endereço minimamente válido.</summary>
    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        // MailAddress aceita alguns formatos exóticos que queremos bloquear
        // (ex.: "foo" sem @). Garantimos presença de @ e domínio com ponto.
        if (!email.Contains('@')) return false;

        try
        {
            var addr = new MailAddress(email.Trim());
            return !string.IsNullOrWhiteSpace(addr.Host)
                   && addr.Host.Contains('.')
                   && addr.Host.Length >= 3;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Lança <see cref="UseCaseValidationException"/> se o email não for válido.</summary>
    public static void EnsureValid(string? email, string campoAmigavel = "Email")
    {
        if (!IsValid(email))
            throw new UseCaseValidationException($"{campoAmigavel} inválido.");
    }
}
