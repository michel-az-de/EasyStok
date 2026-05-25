namespace EasyStock.Application.Ports.Output.Notifications;

public interface IProvedorWhatsApp
{
    string Nome { get; }
    Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default);
}
