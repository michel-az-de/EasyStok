using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.TomTom;

/// <summary>Log comum dos adapters TomTom: 403/429 são chave ou cota, não endereço ruim.</summary>
internal static class TomTomLog
{
    public static void Http(ILogger logger, string api, int status)
    {
        if (status is 403 or 429)
            logger.LogWarning("TomTom {Api} retornou HTTP {Status}", api, status);
        else
            logger.LogDebug("TomTom {Api} retornou HTTP {Status}", api, status);
    }
}
