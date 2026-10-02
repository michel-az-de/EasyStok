using System.Text;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Infra.Async.Email;

/// <summary>SendGrid resolvido. O <c>ToString</c> do record imprimiria a chave da API: aqui ela nunca sai.</summary>
internal sealed record SendGridConfiguracao(string ApiKey, string FromEmail, string? FromName, bool Sandbox)
{
    private bool PrintMembers(StringBuilder construtor)
    {
        construtor.Append("FromEmail = ").Append(FromEmail).Append(", ApiKey = (definida)");
        return true;
    }
}

/// <summary>
/// O provider de e-mail que a configuracao pede, decidido uma vez e igual para a API e o Worker (N3, #1351).
/// <c>Email:Provider</c> explicito (<c>smtp</c>, <c>sendgrid</c>, <c>console</c>) vale como escrito, e chave obrigatoria
/// faltando recusa subir nomeando a chave. Sem ele, <c>smtp</c> quando ha Host, Port e um remetente utilizavel; senao
/// <c>console</c> com aviso nomeando a chave que falta. Nenhum remetente e inventado.
/// </summary>
internal sealed record EscolhaEmail(
    string Provider,
    SmtpConfiguracao? Smtp,
    SendGridConfiguracao? SendGrid,
    IReadOnlyList<string> Avisos)
{
    public const string ProviderSmtp = "smtp";
    public const string ProviderSendGrid = "sendgrid";
    public const string ProviderConsole = "console";

    public static EscolhaEmail Resolver(IConfiguration configuration, string? ambiente)
    {
        var avisos = new List<string>();
        var provider = (configuration["Email:Provider"] ?? string.Empty).Trim().ToLowerInvariant();
        var opcoes = configuration.GetSection(SmtpOpcoes.Secao).Get<SmtpOpcoes>() ?? new SmtpOpcoes();

        switch (provider)
        {
            case ProviderConsole:
                if (SmtpOpcoes.EhProduction(ambiente))
                    avisos.Add("Email:Provider=console em Production: nenhum e-mail será enviado.");
                return ComConsole(avisos);

            case ProviderSendGrid:
                return new EscolhaEmail(ProviderSendGrid, null, LerSendGrid(configuration), avisos);

            case ProviderSmtp:
                return ComSmtp(opcoes.Resolver(ambiente), avisos);

            case "":
                if (opcoes.TentarResolver(ambiente, out var automatica, out var chaveFaltando))
                    return ComSmtp(automatica!, avisos);

                avisos.Add(
                    $"SMTP não configurado ({chaveFaltando} ausente): usando o console, nenhum e-mail será enviado. " +
                    $"Defina {chaveFaltando} (e as demais chaves Smtp) ou Email:Provider=console para silenciar este aviso.");
                return ComConsole(avisos);

            default:
                avisos.Add(
                    $"Email:Provider desconhecido ('{provider}'): use smtp, sendgrid ou console. " +
                    "Usando o console, nenhum e-mail será enviado.");
                return ComConsole(avisos);
        }
    }

    private static EscolhaEmail ComConsole(List<string> avisos) => new(ProviderConsole, null, null, avisos);

    private static EscolhaEmail ComSmtp(SmtpConfiguracao configuracao, List<string> avisos)
    {
        if (configuracao.SegurancaUsaAvisos)
        {
            avisos.Add(
                "Smtp:Seguranca:* ausente (sem FromEmail e sem Username com arroba): os e-mails de segurança " +
                "(reset de senha, conta criada pelo admin) sairão do remetente de avisos.");
        }
        else if (configuracao.Seguranca.ChaveBase == SmtpOpcoes.Secao)
        {
            // From proprio, credencial de avisos: Gmail, SES e outros recusam um From diferente da conta autenticada.
            avisos.Add(
                "Smtp:Seguranca:FromEmail definido sem Smtp:Seguranca:Username: o e-mail de segurança sai com From próprio, " +
                "mas autentica com a credencial de avisos. Muitos provedores recusam um From diferente da conta autenticada; " +
                "defina Smtp:Seguranca:Username e Smtp:Seguranca:Password.");
        }

        return new EscolhaEmail(ProviderSmtp, configuracao, null, avisos);
    }

    private static SendGridConfiguracao LerSendGrid(IConfiguration configuration)
    {
        var secao = configuration.GetSection("SendGrid");

        var apiKey = secao["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("SendGrid:ApiKey é obrigatório quando Email:Provider=sendgrid.");

        var fromEmail = secao["FromEmail"];
        if (string.IsNullOrWhiteSpace(fromEmail))
        {
            throw new InvalidOperationException(
                "SendGrid:FromEmail é obrigatório quando Email:Provider=sendgrid; nenhum remetente é inventado.");
        }

        var sandboxTexto = secao["SandboxMode"];
        var sandbox = false;
        if (!string.IsNullOrWhiteSpace(sandboxTexto) && !bool.TryParse(sandboxTexto.Trim(), out sandbox))
            throw new InvalidOperationException($"SendGrid:SandboxMode inválida ('{sandboxTexto.Trim()}'): use true ou false.");

        var nome = secao["FromName"];
        return new SendGridConfiguracao(apiKey, fromEmail.Trim(), string.IsNullOrWhiteSpace(nome) ? null : nome.Trim(), sandbox);
    }
}
