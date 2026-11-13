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
    /// Mínimo da senha da conta de plataforma (SuperAdmin) semeada em produção: acima do <see cref="MinLength"/> dos usuários
    /// (OWASP recomenda 12). O seed e a política leem a mesma constante.
    /// </summary>
    public const int MinLengthDaContaDePlataforma = 12;

    /// <summary>
    /// Valida se a senha atende à política de segurança.
    /// Regras: de 10 a 128 caracteres, pelo menos uma maiúscula, minúscula, número e caractere especial.
    /// </summary>
    public static bool IsValid(string? password) => Falhas(password).Count == 0;

    /// <summary>
    /// #2018: os critérios que a senha não cumpre, vazio quando cumpre todos. É a ÚNICA regra: <see cref="IsValid"/> e os
    /// validators (<c>SenhaRules.AplicarPoliticaSenha</c>) leem daqui, cada um com a sua mensagem. Letra e dígito são os do
    /// Unicode (Ç é maiúscula) e símbolo é tudo que não é letra nem dígito (o sublinhado conta).
    /// </summary>
    public static IReadOnlyList<FalhaDeSenha> Falhas(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return [FalhaDeSenha.Vazia];

        var falhas = new List<FalhaDeSenha>();
        if (password.Length < MinLength) falhas.Add(FalhaDeSenha.Curta);
        if (password.Length > MaxLength) falhas.Add(FalhaDeSenha.Longa);
        if (!password.Any(char.IsUpper)) falhas.Add(FalhaDeSenha.SemMaiuscula);
        if (!password.Any(char.IsLower)) falhas.Add(FalhaDeSenha.SemMinuscula);
        if (!password.Any(char.IsDigit)) falhas.Add(FalhaDeSenha.SemNumero);
        if (!password.Any(ch => !char.IsLetterOrDigit(ch))) falhas.Add(FalhaDeSenha.SemSimbolo);
        return falhas;
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

/// <summary>#2018: critério da <see cref="PasswordPolicy"/> que a senha não cumpre.</summary>
public enum FalhaDeSenha
{
    Vazia,
    Curta,
    Longa,
    SemMaiuscula,
    SemMinuscula,
    SemNumero,
    SemSimbolo,
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
