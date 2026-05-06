using EasyStock.Application.Ports.Output.Notifications;
using Microsoft.Extensions.Logging;
using Scriban;

namespace EasyStock.Infra.Notifications.Templating;

public sealed class ScribanRenderer(ILogger<ScribanRenderer> logger) : IRendererTemplate
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromMilliseconds(500);

    public async Task<string> RenderizarAsync(
        string templateText,
        IDictionary<string, object?> variaveis,
        CancellationToken ct = default)
    {
        var template = Template.Parse(templateText);

        if (template.HasErrors)
        {
            var erros = string.Join("; ", template.Messages.Select(m => m.Message));
            logger.LogWarning("Template com erros de parse: {Erros}", erros);
            throw new InvalidOperationException($"Template inválido: {erros}");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RenderTimeout);

        try
        {
            var context = ScribanSandbox.CriarContexto(variaveis);
            var resultado = await template.RenderAsync(context);
            return resultado ?? string.Empty;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Timeout ao renderizar template (>{Timeout}ms)", RenderTimeout.TotalMilliseconds);
            throw new TimeoutException($"Renderização de template excedeu {RenderTimeout.TotalMilliseconds}ms.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Erro ao renderizar template Scriban");
            throw new InvalidOperationException($"Erro na renderização: {ex.Message}", ex);
        }
    }
}
