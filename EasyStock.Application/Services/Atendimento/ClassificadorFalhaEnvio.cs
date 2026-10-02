using EasyStock.Application.Ports.Output.Atendimento;
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
}
