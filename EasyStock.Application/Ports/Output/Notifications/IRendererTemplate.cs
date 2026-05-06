namespace EasyStock.Application.Ports.Output.Notifications;

public interface IRendererTemplate
{
    Task<string> RenderizarAsync(
        string template,
        IDictionary<string, object?> variaveis,
        CancellationToken ct = default);
}
