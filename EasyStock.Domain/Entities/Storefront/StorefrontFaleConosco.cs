using EasyStock.Domain.Exceptions;

namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Canal alternativo ao WhatsApp para contato do cliente (público) com a Babá.
///
/// <para>
/// Aceita mensagens de clientes autenticados (<see cref="ClienteId"/> setado)
/// ou anônimos (<see cref="ClienteId"/> = null). O telefone é obrigatório
/// para resposta — e-mail é opcional. Babá responde fora do sistema
/// (telefone/e-mail/WhatsApp) e marca como <see cref="MarcarRespondido"/>.
/// </para>
/// </summary>
public class StorefrontFaleConosco
{
    private const int LimiteAssunto = 100;
    private const int LimiteMensagem = 2000;
    private const int LimiteNome = 120;
    private const int LimiteTelefone = 30;
    private const int LimiteEmail = 254;
    private const int LimiteRespondidoPor = 120;

    public Guid Id { get; private set; }
    public Guid StorefrontId { get; private set; }

    /// <summary>Cliente autenticado que enviou — null se anônimo.</summary>
    public Guid? ClienteId { get; private set; }

    public string Nome { get; private set; } = null!;
    public string Telefone { get; private set; } = null!;

    /// <summary>E-mail opcional. Null se não informado.</summary>
    public string? Email { get; private set; }

    public string Assunto { get; private set; } = null!;
    public string Mensagem { get; private set; } = null!;

    public DateTime CriadoEm { get; private set; }

    public bool Respondido { get; private set; }
    public string? RespondidoPor { get; private set; }
    public DateTime? RespondidoEm { get; private set; }

    private StorefrontFaleConosco() { }

    public static StorefrontFaleConosco Criar(
        Guid storefrontId,
        Guid? clienteId,
        string nome,
        string telefone,
        string? email,
        string assunto,
        string mensagem)
    {
        if (storefrontId == Guid.Empty)
            throw new RegraDeDominioVioladaException("StorefrontId é obrigatório.");

        if (clienteId == Guid.Empty)
            throw new RegraDeDominioVioladaException(
                "ClienteId não pode ser Guid.Empty — use null para anônimo.");

        var nomeTrim = ValidarObrigatorio(nome, "Nome", LimiteNome);
        var telefoneTrim = ValidarObrigatorio(telefone, "Telefone", LimiteTelefone);
        var assuntoTrim = ValidarObrigatorio(assunto, "Assunto", LimiteAssunto);
        var mensagemTrim = ValidarObrigatorio(mensagem, "Mensagem", LimiteMensagem);
        var emailTrim = NormalizarEmail(email);

        return new StorefrontFaleConosco
        {
            Id = Guid.NewGuid(),
            StorefrontId = storefrontId,
            ClienteId = clienteId,
            Nome = nomeTrim,
            Telefone = telefoneTrim,
            Email = emailTrim,
            Assunto = assuntoTrim,
            Mensagem = mensagemTrim,
            CriadoEm = DateTime.UtcNow,
            Respondido = false,
            RespondidoPor = null,
            RespondidoEm = null,
        };
    }

    /// <summary>
    /// Marca a mensagem como respondida pela Babá (fora do sistema).
    /// Idempotente — primeira chamada é canônica.
    /// </summary>
    public void MarcarRespondido(string respondidoPor)
    {
        if (Respondido) return;

        if (string.IsNullOrWhiteSpace(respondidoPor))
            throw new RegraDeDominioVioladaException("RespondidoPor é obrigatório.");

        var trim = respondidoPor.Trim();
        if (trim.Length > LimiteRespondidoPor)
            throw new RegraDeDominioVioladaException(
                $"RespondidoPor não pode exceder {LimiteRespondidoPor} caracteres (recebido: {trim.Length}).");

        Respondido = true;
        RespondidoPor = trim;
        RespondidoEm = DateTime.UtcNow;
    }

    private static string ValidarObrigatorio(string valor, string campo, int limite)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new RegraDeDominioVioladaException($"{campo} é obrigatório.");

        var trim = valor.Trim();
        if (trim.Length > limite)
            throw new RegraDeDominioVioladaException(
                $"{campo} não pode exceder {limite} caracteres (recebido: {trim.Length}).");

        return trim;
    }

    private static string? NormalizarEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        var trim = email.Trim();
        if (trim.Length > LimiteEmail)
            throw new RegraDeDominioVioladaException(
                $"Email não pode exceder {LimiteEmail} caracteres (recebido: {trim.Length}).");

        return trim;
    }
}
