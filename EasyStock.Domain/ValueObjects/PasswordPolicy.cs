// Política centralizada de validação de senhas
// Define as regras de segurança que toda senha deve cumprir na aplicação
// Fonte única de verdade para validação de credenciais

namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// Encapsula as regras de política de senha para a aplicação.
/// Centraliza em um único lugar as validações de segurança de credenciais.
/// </summary>
public static class PasswordPolicy
{
    // Comprimento mínimo (OWASP recomenda mínimo 12, usar 10 como padrão neste projeto)
    public const int MinLength = 10;

    // Comprimento máximo (proteção contra DoS)
    public const int MaxLength = 128;

    /// <summary>
    /// Valida se a senha atende à política de segurança.
    /// Regras: mínimo 10 caracteres, pelo menos uma maiúscula, minúscula, número e caractere especial.
    /// </summary>
    public static bool IsValid(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        if (password.Length < MinLength || password.Length > MaxLength)
            return false;

        // Verifica presença de maiúscula
        if (!password.Any(char.IsUpper))
            return false;

        // Verifica presença de minúscula
        if (!password.Any(char.IsLower))
            return false;

        // Verifica presença de dígito
        if (!password.Any(char.IsDigit))
            return false;

        // Verifica presença de caractere especial (não é alfanumérico)
        if (!password.Any(ch => !char.IsLetterOrDigit(ch)))
            return false;

        return true;
    }

    /// <summary>
    /// Retorna uma descrição legível dos requisitos da política.
    /// Usado para mensagens de erro em formulários.
    /// </summary>
    public static string GetRequirementsText()
    {
        return $"A senha deve ter no mínimo {MinLength} caracteres e conter " +
               "pelo menos uma letra maiúscula, uma minúscula, um número e um caractere especial.";
    }

    /// <summary>
    /// Valida força da senha e retorna um nível de segurança (fraco, médio, forte).
    /// Usado para feedback visual do usuário.
    /// </summary>
    public static PasswordStrength CalculateStrength(string? password)
    {
        if (!IsValid(password))
            return PasswordStrength.Invalid;

        // Conta critérios adicionais para melhor força
        int strengthScore = 1; // Já atende aos 4 requisitos básicos

        if (password!.Length >= 12) strengthScore++;
        if (password.Length >= 16) strengthScore++;
        if (password.Any(ch => char.IsSymbol(ch) || char.IsPunctuation(ch))) strengthScore++; // Símbolos extras

        return strengthScore switch
        {
            1 or 2 => PasswordStrength.Weak,
            3 or 4 => PasswordStrength.Medium,
            _ => PasswordStrength.Strong
        };
    }
}

/// <summary>
/// Níveis de força de senha.
/// </summary>
public enum PasswordStrength
{
    Invalid = 0,
    Weak = 1,
    Medium = 2,
    Strong = 3
}
