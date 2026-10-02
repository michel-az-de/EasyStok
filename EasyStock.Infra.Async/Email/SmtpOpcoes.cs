using System.Globalization;
using MimeKit;

namespace EasyStock.Infra.Async.Email;

/// <summary>Chaves de uma caixa de remetente: <c>Smtp:Seguranca:*</c>.</summary>
public sealed class SmtpRemetenteOpcoes
{
    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? FromEmail { get; set; }

    public string? FromName { get; set; }
}

/// <summary>
/// Secao <c>Smtp</c> como vem da configuracao, sem validacao. Numeros e flags ficam como texto de proposito: o binder
/// lancaria com mensagem generica, e aqui a mensagem nomeia a chave (<see cref="Resolver"/>). Os nomes legados do
/// <c>.env</c> (<c>Smtp__Host</c>, <c>Port</c>, <c>Username</c>, <c>Password</c>, <c>FromEmail</c>, <c>FromName</c>,
/// <c>EnableSsl</c>) continuam valendo.
/// </summary>
public sealed class SmtpOpcoes
{
    public const string Secao = "Smtp";

    private const int TimeoutPadraoSegundos = 20;

    public string? Host { get; set; }

    public string? Port { get; set; }

    /// <summary><c>Auto</c> (padrao), <c>SslImplicito</c>, <c>StartTls</c> ou <c>Nenhum</c>.</summary>
    public string? Modo { get; set; }

    public string? TimeoutSegundos { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? FromEmail { get; set; }

    public string? FromName { get; set; }

    /// <summary>Legado: so lido quando <see cref="Modo"/> nao existe. <c>false</c> equivale a <c>Nenhum</c>.</summary>
    public string? EnableSsl { get; set; }

    /// <summary>Caixa de seguranca. Sem ela (nem <c>FromEmail</c>, nem <c>Username</c> com arroba) cai na de avisos.</summary>
    public SmtpRemetenteOpcoes? Seguranca { get; set; }

    /// <summary>
    /// Valida e resolve. Valor presente e invalido, ou <c>Nenhum</c> em Production, lanca
    /// <see cref="InvalidOperationException"/> nomeando a chave. Chave obrigatoria ausente tambem lanca (use
    /// <see cref="TentarResolver"/> para decidir pelo console em vez de recusar).
    /// </summary>
    public SmtpConfiguracao Resolver(string? ambiente)
    {
        if (TentarResolver(ambiente, out var configuracao, out var chaveFaltando))
            return configuracao!;

        throw new InvalidOperationException(
            $"{chaveFaltando} é obrigatório para enviar e-mail por SMTP (seção {Secao}); nenhum servidor ou remetente é inventado.");
    }

    /// <summary>
    /// Como <see cref="Resolver"/>, mas chave obrigatoria ausente devolve <c>false</c> e o nome dela em
    /// <paramref name="chaveFaltando"/>. Valor invalido continua lancando: quem escreveu a chave queria SMTP.
    /// </summary>
    public bool TentarResolver(string? ambiente, out SmtpConfiguracao? configuracao, out string? chaveFaltando)
    {
        configuracao = null;
        chaveFaltando = null;

        // 1. Valor presente e invalido sempre recusa, mesmo que falte outra chave.
        var porta = LerInteiro(Port, $"{Secao}:Port", 1, 65535);
        var timeoutSegundos = LerInteiro(TimeoutSegundos, $"{Secao}:TimeoutSegundos", 1, 300) ?? TimeoutPadraoSegundos;
        var modoDeclarado = LerModo(out var modoVeioDoLegado);
        var fromEmail = ValidarEndereco(Normalizar(FromEmail), $"{Secao}:FromEmail");
        var fromEmailSeguranca = ValidarEndereco(Normalizar(Seguranca?.FromEmail), $"{Secao}:Seguranca:FromEmail");

        // Nunca "Auto" para o transporte: 465 e implicito, qualquer outra porta exige STARTTLS.
        var modo = modoDeclarado == SmtpModo.Auto
            ? (porta == 465 ? SmtpModo.SslImplicito : SmtpModo.StartTls)
            : modoDeclarado;

        if (modo == SmtpModo.Nenhum && EhProduction(ambiente))
        {
            throw new InvalidOperationException(modoVeioDoLegado
                ? $"{Secao}:EnableSsl=false equivale a {Secao}:Modo=Nenhum, que não é permitido em Production: o e-mail sairia sem TLS. Remova a chave ou use {Secao}:Modo=StartTls."
                : $"{Secao}:Modo=Nenhum não é permitido em Production: o e-mail sairia sem TLS. Use {Secao}:Modo=StartTls ou SslImplicito.");
        }

        // 2. Chaves obrigatorias: nada de servidor, porta ou remetente inventado.
        var host = Normalizar(Host);
        if (host is null)
        {
            chaveFaltando = $"{Secao}:Host";
            return false;
        }

        if (porta is null)
        {
            chaveFaltando = $"{Secao}:Port";
            return false;
        }

        var usuario = Normalizar(Username);
        var enderecoAvisos = fromEmail ?? EnderecoDe(usuario, $"{Secao}:Username", $"{Secao}:FromEmail");
        if (enderecoAvisos is null)
        {
            chaveFaltando = $"{Secao}:FromEmail";
            return false;
        }

        // 3. Remetentes. Cada caixa autentica com a propria credencial.
        var avisos = new SmtpRemetente(enderecoAvisos, Normalizar(FromName), usuario, SenhaOuNula(Password), Secao);

        var usuarioSeguranca = Normalizar(Seguranca?.Username);
        var enderecoSeguranca = fromEmailSeguranca ?? EnderecoDe(usuarioSeguranca, $"{Secao}:Seguranca:Username", $"{Secao}:Seguranca:FromEmail");
        SmtpRemetente seguranca;
        bool segurancaUsaAvisos;
        if (enderecoSeguranca is null)
        {
            seguranca = avisos;
            segurancaUsaAvisos = true;
        }
        else
        {
            var propriaCredencial = usuarioSeguranca is not null;
            seguranca = new SmtpRemetente(
                enderecoSeguranca,
                Normalizar(Seguranca?.FromName) ?? avisos.Nome,
                propriaCredencial ? usuarioSeguranca : avisos.Username,
                propriaCredencial ? SenhaOuNula(Seguranca?.Password) : avisos.Password,
                propriaCredencial ? $"{Secao}:Seguranca" : Secao);
            segurancaUsaAvisos = false;
        }

        configuracao = new SmtpConfiguracao(
            host, porta.Value, modo, TimeSpan.FromSeconds(timeoutSegundos), avisos, seguranca, segurancaUsaAvisos);
        return true;
    }

    private SmtpModo LerModo(out bool veioDoLegado)
    {
        veioDoLegado = false;

        if (!string.IsNullOrWhiteSpace(Modo))
        {
            return Modo.Trim().ToLowerInvariant() switch
            {
                "auto" => SmtpModo.Auto,
                "sslimplicito" => SmtpModo.SslImplicito,
                "starttls" => SmtpModo.StartTls,
                "nenhum" => SmtpModo.Nenhum,
                _ => throw new InvalidOperationException(
                    $"{Secao}:Modo inválido ('{Modo.Trim()}'): use Auto, SslImplicito, StartTls ou Nenhum."),
            };
        }

        // Legado: so vale quando Smtp:Modo nao existe.
        if (!string.IsNullOrWhiteSpace(EnableSsl))
        {
            if (!bool.TryParse(EnableSsl.Trim(), out var ssl))
                throw new InvalidOperationException($"{Secao}:EnableSsl inválida ('{EnableSsl.Trim()}'): use true ou false.");

            veioDoLegado = !ssl;
            return ssl ? SmtpModo.Auto : SmtpModo.Nenhum;
        }

        return SmtpModo.Auto;
    }

    private static int? LerInteiro(string? valor, string chave, int minimo, int maximo)
    {
        var texto = Normalizar(valor);
        if (texto is null)
            return null;

        if (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var numero)
            || numero < minimo || numero > maximo)
        {
            throw new InvalidOperationException(
                $"{chave} inválida ('{texto}'): informe um número inteiro de {minimo} a {maximo}.");
        }

        return numero;
    }

    private static string? ValidarEndereco(string? endereco, string chave)
    {
        if (endereco is null)
            return null;

        if (!MailboxAddress.TryParse(endereco, out var caixa) || !caixa.Address.Contains('@'))
            throw new InvalidOperationException($"{chave} inválido ('{endereco}'): informe um endereço de e-mail.");

        return caixa.Address;
    }

    // Sem FromEmail, o Username com arroba serve de remetente. Se ele nao for um endereco valido, estoura na subida
    // nomeando a chave, em vez de virar "destinatario invalido" em cada envio.
    private static string? EnderecoDe(string? username, string chaveDoUsername, string chaveDoRemetente)
    {
        if (username is null || !username.Contains('@'))
            return null;

        if (!MailboxAddress.TryParse(username, out var caixa) || !caixa.Address.Contains('@'))
        {
            throw new InvalidOperationException(
                $"{chaveDoUsername} ('{username}') não é um endereço de e-mail válido e, sem {chaveDoRemetente}, ele serve de remetente: " +
                $"corrija a chave ou defina {chaveDoRemetente}.");
        }

        return caixa.Address;
    }

    private static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    // Senha nao leva trim: espaco pode fazer parte dela.
    private static string? SenhaOuNula(string? senha) => string.IsNullOrEmpty(senha) ? null : senha;

    // Sem ambiente conhecido vale o fail-safe: Production.
    internal static bool EhProduction(string? ambiente) =>
        string.IsNullOrWhiteSpace(ambiente) || string.Equals(ambiente.Trim(), "Production", StringComparison.OrdinalIgnoreCase);
}
