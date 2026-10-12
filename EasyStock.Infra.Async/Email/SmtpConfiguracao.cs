using System.Text;

namespace EasyStock.Infra.Async.Email;

/// <summary>Como a conexao SMTP protege o transporte, ja resolvido (nunca "Auto": isso e so uma entrada).</summary>
public enum SmtpModo
{
    /// <summary>Entrada: 465 vira <see cref="SslImplicito"/>, qualquer outra porta vira <see cref="StartTls"/>.</summary>
    Auto,

    /// <summary>TLS ja na conexao (porta 465, SMTPS).</summary>
    SslImplicito,

    /// <summary>STARTTLS obrigatorio: sem ele o envio falha, nunca cai em texto puro.</summary>
    StartTls,

    /// <summary>Texto puro. So fora de Production (Mailpit de desenvolvimento).</summary>
    Nenhum,
}

/// <summary>
/// Uma caixa de saida resolvida: endereco e nome do <c>From</c> mais a credencial com que ela autentica.
/// <paramref name="ChaveBase"/> e o prefixo das chaves de configuracao dessa credencial (<c>Smtp</c> ou
/// <c>Smtp:Seguranca</c>), para o log apontar a chave certa sem nunca mostrar a senha.
/// </summary>
public sealed record SmtpRemetente(string Email, string? Nome, string? Username, string? Password, string ChaveBase)
{
    // O ToString do record imprimiria a senha. Nenhum log, assert ou dump acidental pode vaza-la.
    private bool PrintMembers(StringBuilder construtor)
    {
        construtor.Append("Email = ").Append(Email)
            .Append(", ChaveBase = ").Append(ChaveBase)
            .Append(", Credencial = ").Append(string.IsNullOrEmpty(Username) ? "(nenhuma)" : "(definida)");
        return true;
    }
}

/// <summary>
/// Configuracao SMTP validada e resolvida a partir da secao <c>Smtp</c> (<see cref="SmtpOpcoes.Resolver"/>). E a mesma
/// para a API e o Worker, porque os dois constroem o e-mail pela mesma fabrica.
/// </summary>
public sealed record SmtpConfiguracao(
    string Host,
    int Porta,
    SmtpModo Modo,
    TimeSpan Timeout,
    SmtpRemetente Avisos,
    SmtpRemetente Seguranca,
    bool SegurancaUsaAvisos);
