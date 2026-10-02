using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Notifications;
using MailKit.Net.Smtp;
using MailKitAuthenticationException = MailKit.Security.AuthenticationException;

namespace EasyStock.Infra.Async.Email;

/// <summary>Falha de envio ja classificada: <paramref name="Detalhe"/> vai para o banco e e curto e sem endereco.</summary>
internal sealed record FalhaSmtp(DesfechoEnvio Desfecho, int? CodigoSmtp, bool ErroDeConfiguracao, string Detalhe);

/// <summary>
/// Classificacao por protocolo (N3, #1351). 5xx do servidor e permanente; 4xx, rede, protocolo e teto estourado sao
/// transitorios; autenticacao recusada (530, 534, 535, 538) e permanente e e erro de configuracao. O detalhe nunca leva
/// o texto livre do servidor sem antes tirar endereco de e-mail, e nunca leva a senha.
/// </summary>
internal static partial class ClassificadorFalhaSmtp
{
    private const int LimiteDoDetalhe = 200;
    private const string TempoLimite = "tempo limite excedido no envio SMTP";

    public static FalhaSmtp Classificar(Exception excecao, bool tetoEstourado, string chaveBase = "Smtp")
    {
        switch (excecao)
        {
            case SmtpCommandException comando:
                return DeComando((int)comando.StatusCode, comando.Message, chaveBase);

            case MailKitAuthenticationException autenticacao:
                return AutenticacaoRecusada(CodigoNoInicio(autenticacao.Message) ?? 535, chaveBase);

            case OperationCanceledException:
                // Aqui so chega o teto: o cancelamento do chamador o servico propaga antes de classificar.
                return Transitoria(tetoEstourado ? TempoLimite : "envio SMTP cancelado");

            case TimeoutException:
                return Transitoria(TempoLimite);

            case NotSupportedException:
                // O servidor nao ofereceu STARTTLS ou AUTH. Nao rebaixa para texto puro (o envio nao sai),
                // mas pode ser queda momentanea ou um atacante: tenta de novo, e o log aponta as chaves.
                return new FalhaSmtp(
                    DesfechoEnvio.FalhaTransitoria,
                    null,
                    true,
                    "O servidor SMTP não ofereceu STARTTLS ou AUTH: confira Smtp:Modo, Smtp:Port e Smtp:Host.");

            case MensagemEmailInvalidaException:
                // Endereco ou anexo malformado: nenhuma tentativa futura conserta.
                return new FalhaSmtp(
                    DesfechoEnvio.FalhaPermanente,
                    null,
                    false,
                    "Mensagem recusada antes do envio: destinatário, remetente ou anexo inválido.");

            default:
                return Transitoria($"{excecao.GetType().Name}: {Sanitizar(excecao.Message)}");
        }
    }

    private static FalhaSmtp Transitoria(string detalhe) =>
        new(DesfechoEnvio.FalhaTransitoria, null, false, Limitar(detalhe));

    private static FalhaSmtp DeComando(int codigo, string mensagemDoServidor, string chaveBase)
    {
        if (codigo is 530 or 534 or 535 or 538)
            return AutenticacaoRecusada(codigo, chaveBase);

        var desfecho = codigo >= 500 ? DesfechoEnvio.FalhaPermanente : DesfechoEnvio.FalhaTransitoria;
        return new FalhaSmtp(desfecho, codigo, false, Limitar($"SMTP {codigo}: {Sanitizar(mensagemDoServidor)}"));
    }

    private static FalhaSmtp AutenticacaoRecusada(int codigo, string chaveBase) =>
        new(
            DesfechoEnvio.FalhaPermanente,
            codigo,
            true,
            $"Autenticação SMTP recusada ({codigo}): confira {chaveBase}:Username e {chaveBase}:Password."
            // 530 tambem e "Must issue a STARTTLS command first": ai a chave certa seria a do modo.
            + (codigo == 530 ? " Se o servidor exige TLS, confira também Smtp:Modo." : string.Empty));

    private static int? CodigoNoInicio(string? mensagem)
    {
        var correspondencia = CodigoInicial().Match(mensagem ?? string.Empty);
        return correspondencia.Success ? int.Parse(correspondencia.Groups[1].Value) : null;
    }

    private static string Sanitizar(string? texto)
    {
        var semEndereco = EnderecoDeEmail().Replace(texto ?? string.Empty, "[e-mail]");
        return Espacos().Replace(semEndereco, " ").Trim();
    }

    private static string Limitar(string texto) =>
        texto.Length <= LimiteDoDetalhe ? texto : texto[..(LimiteDoDetalhe - 1)] + "…";

    [GeneratedRegex(@"^\s*(\d{3})")]
    private static partial Regex CodigoInicial();

    [GeneratedRegex(@"[^\s<>""',;:()\[\]]+@[^\s<>""',;:()\[\]]+")]
    private static partial Regex EnderecoDeEmail();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();
}
