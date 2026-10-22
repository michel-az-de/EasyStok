using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// S57 (#1355): traduz a exceção do canal no <see cref="TipoFalhaEnvio"/> que decide o reenvio automático.
/// Timeout é incerto (a mensagem pode ter saído; reenviar duplicaria, #1292). Erro desconhecido não insiste.
/// #1915: também é incerto o que a Meta pode ter recebido: 5xx, 2xx sem message id e conexão que caiu depois do POST.
/// </summary>
public static class ClassificadorFalhaEnvio
{
    public static TipoFalhaEnvio Classificar(Exception ex) => ex switch
    {
        WhatsAppCloudException { StatusHttp: >= 200 and < 300 or >= 500 } => TipoFalhaEnvio.Incerta,
        WhatsAppCloudException meta => meta.EhPermanente ? TipoFalhaEnvio.Permanente : TipoFalhaEnvio.Temporaria,
        TimeoutException or TaskCanceledException => TipoFalhaEnvio.Incerta,
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError
            or HttpRequestError.SecureConnectionError } => TipoFalhaEnvio.Temporaria,
        HttpRequestException => TipoFalhaEnvio.Incerta,
        _ => TipoFalhaEnvio.Permanente,
    };

    /// <summary>
    /// #1411: códigos do callback <c>failed</c> sabidamente temporários (erro genérico, limite de taxa, spam rate,
    /// par de envios, serviço indisponível). A tabela de notificações (<see cref="CodigosErroMeta"/>) trata código
    /// fora dela como transitório; aqui é o contrário, porque reenviar sozinho ao cliente exige certeza.
    /// </summary>
    private static readonly HashSet<int> CodigosTemporarios = [131000, 130429, 131016, 131056, 133004];

    /// <summary>
    /// #1396: código do callback <c>failed</c> da Meta. Só insiste em código da lista de temporários;
    /// desconhecido ou sem código não insiste.
    /// </summary>
    public static TipoFalhaEnvio ClassificarCodigoMeta(int? codigo) =>
        codigo is { } c && CodigosTemporarios.Contains(c) ? TipoFalhaEnvio.Temporaria : TipoFalhaEnvio.Permanente;

    /// <summary>
    /// #1765-11: o que fica em <c>Mensagem.Erro</c>. Frase estável em português com o código do canal (o console traduz pelo
    /// código: 131026, 131047...); o texto do provedor, em inglês, vai só ao log de quem chama. Erro escrito por nós (sem
    /// código e sem resposta da Meta) já está em português e segue como veio.
    /// </summary>
    public static string Motivo(Exception ex) => ex switch
    {
        WhatsAppCloudException { Codigo: > 0 } meta => MotivoCodigoMeta(meta.Codigo),
        WhatsAppCloudException { StatusHttp: >= 200 and < 300 } => "O canal aceitou sem devolver o id da mensagem (CANAL_SEM_ID).",
        WhatsAppCloudException { StatusHttp: { } status } => $"O canal respondeu com erro (HTTP {status}).",
        WhatsAppCloudException propria => propria.Message,
        TimeoutException or TaskCanceledException => "O canal não respondeu a tempo (CANAL_SEM_RESPOSTA).",
        HttpRequestException => "Sem conexão com o canal (CANAL_SEM_CONEXAO).",
        RegraDeDominioVioladaException or UseCaseValidationException => ex.Message,
        _ => "Falha no canal (CANAL_ERRO).",
    };

    /// <summary>#1765-11: o callback <c>failed</c> da Meta traz o título em inglês; grava-se o código.</summary>
    public static string MotivoCodigoMeta(int? codigo) =>
        codigo is { } c and > 0 ? $"Falha no canal (código Meta {c})." : "Falha no canal (sem código da Meta).";
}
