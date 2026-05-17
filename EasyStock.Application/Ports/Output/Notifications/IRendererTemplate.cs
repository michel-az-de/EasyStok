namespace EasyStock.Application.Ports.Output.Notifications;

public interface IRendererTemplate
{
    Task<string> RenderizarAsync(
        string template,
        IDictionary<string, object?> variaveis,
        CancellationToken ct = default);

    /// <summary>
    /// Renderiza com auto-escape de strings em entidades HTML antes da interpolacao.
    /// Usar para canais que renderizam HTML (Email, InApp HTML) e variaveis vem de payload
    /// nao confiavel — evita XSS em clientes de email/UI.
    /// </summary>
    Task<string> RenderizarAsync(
        string template,
        IDictionary<string, object?> variaveis,
        bool htmlEscape,
        CancellationToken ct = default);
}
