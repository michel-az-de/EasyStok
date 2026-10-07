using System.Text.RegularExpressions;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Nome e contato que o visitante do chat do site escreveu no formulário antes de conversar (#1430).
/// Não é cadastro: fica marcado como "informado pelo visitante" até a loja confirmar pela Ficha. Nunca
/// liga a conversa a um cliente existente pelo telefone (quem digita um número alheio não vira o dono dele).
/// Só existe com o aceite da política de privacidade; <see cref="InformadoEm"/> é o instante desse aceite.
/// </summary>
public sealed record ContatoInformadoVisitante
{
    public const int NomeTamanhoMinimo = 2;
    public const int NomeTamanhoMaximo = Conversa.ContatoNomeTamanhoMaximo;
    public const int EmailTamanhoMaximo = 254;

    private static readonly Regex Espacos = new(@"\s+", RegexOptions.Compiled);

    public string Nome { get; }

    /// <summary>E.164 BR com <c>+</c> (<see cref="TelefoneE164"/>).</summary>
    public string Telefone { get; }

    /// <summary>Minúsculo, sem espaços nas pontas; nulo quando o visitante não informou.</summary>
    public string? Email { get; }

    public DateTime InformadoEm { get; }

    private ContatoInformadoVisitante(string nome, string telefone, string? email, DateTime informadoEm)
    {
        Nome = nome;
        Telefone = telefone;
        Email = email;
        InformadoEm = informadoEm;
    }

    /// <summary>Valida o que veio do formulário. Lança <see cref="RegraDeDominioVioladaException"/> com a frase para o visitante.</summary>
    public static ContatoInformadoVisitante Criar(string? nome, string? telefone, string? email, bool aceitePrivacidade, DateTime agora)
    {
        if (!aceitePrivacidade)
            throw new RegraDeDominioVioladaException("Para conversar, aceite a política de privacidade.");

        var nomeLimpo = Espacos.Replace(nome ?? string.Empty, " ").Trim();
        if (nomeLimpo.Length < NomeTamanhoMinimo)
            throw new RegraDeDominioVioladaException("Informe seu nome.");
        if (nomeLimpo.Length > NomeTamanhoMaximo || nomeLimpo.Any(char.IsControl))
            throw new RegraDeDominioVioladaException("Nome inválido.");

        string telefoneE164;
        try
        {
            telefoneE164 = TelefoneE164.From(telefone).Value;
        }
        catch (TelefoneInvalidoException)
        {
            throw new RegraDeDominioVioladaException("Telefone inválido: informe DDD e número.");
        }

        return new ContatoInformadoVisitante(nomeLimpo, telefoneE164, EmailNormalizado(email), Utc(agora));
    }

    /// <summary>Reconstrói o que já foi validado e gravado (sessão ou conversa).</summary>
    public static ContatoInformadoVisitante? Gravado(string? nome, string? telefone, string? email, DateTime? informadoEm) =>
        informadoEm is { } em && !string.IsNullOrWhiteSpace(telefone)
            ? new ContatoInformadoVisitante(nome ?? string.Empty, telefone, email, Utc(em))
            : null;

    /// <summary>E-mail opcional: vazio vira nulo; preenchido precisa ser válido.</summary>
    public static string? EmailNormalizado(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;
        var valido = EmailAddress.TryFrom(email);
        if (valido is null || valido.Value.Length > EmailTamanhoMaximo)
            throw new RegraDeDominioVioladaException("E-mail inválido.");
        return valido.Value;
    }

    private static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };
}
