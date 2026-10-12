using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// S57 (#1355): traduz a exceção do canal no <see cref="TipoFalhaEnvio"/> que decide o reenvio automático.
/// Timeout é incerto (a mensagem pode ter saído; reenviar duplicaria, #1292). Erro desconhecido não insiste.
/// </summary>
public static class ClassificadorFalhaEnvio
{
    public static TipoFalhaEnvio Classificar(Exception ex) => ex switch
    {
        WhatsAppCloudException meta => meta.EhPermanente ? TipoFalhaEnvio.Permanente : TipoFalhaEnvio.Temporaria,
        TimeoutException or TaskCanceledException => TipoFalhaEnvio.Incerta,
        HttpRequestException => TipoFalhaEnvio.Temporaria,
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
}
