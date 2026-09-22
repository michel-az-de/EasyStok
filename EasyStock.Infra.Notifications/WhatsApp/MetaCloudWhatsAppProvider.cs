using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Notifications.WhatsApp;

/// <summary>
/// Delega ao <see cref="IWhatsAppCloudClient"/> (S02) — não fala HTTP direto com a Meta.
/// Retry de rede/timeout é responsabilidade do pipeline Polly de dentro do cliente; erro de
/// aplicação da Meta (<see cref="WhatsAppCloudException"/>) vira <see cref="ResultadoEnvio"/> com
/// falha, sem exceção subir para o dispatcher de notificações.
/// </summary>
public sealed class MetaCloudWhatsAppProvider(
    IWhatsAppCloudClient cloudClient,
    ILogger<MetaCloudWhatsAppProvider> logger) : IProvedorWhatsApp
{
    public string Nome => "meta";

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await cloudClient.EnviarTextoAsync(mensagem.Destinatario, mensagem.Corpo, ct: ct);
            sw.Stop();
            return new ResultadoEnvio(Sucesso: true, ProviderUsado: "meta", DuracaoMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Falha Meta WhatsApp para {Destinatario}", mensagem.Destinatario);
            return new ResultadoEnvio(Sucesso: false, ProviderUsado: "meta",
                ErroDetalhado: ex.Message, DuracaoMs: sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Onda 2.1 — envia mensagem via template aprovado na Meta Business Manager.
    /// Templates exigem aprovacao previa (24-72h); fora da janela de 24h pos-ultima-resposta-do-cliente,
    /// SO templates podem ser enviados (utility/marketing/authentication categories).
    ///
    /// <para>
    /// Variaveis sao posicionais ({{1}}, {{2}}, ...) na ordem definida no template aprovado.
    /// languageCode segue padrao IETF (pt_BR, en_US).
    /// </para>
    /// </summary>
    public async Task<ResultadoEnvio> EnviarTemplateAsync(
        string destino,
        string templateName,
        IReadOnlyList<string> vars,
        string languageCode = "pt_BR",
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await cloudClient.EnviarTemplateAsync(destino, templateName, languageCode, vars, ct: ct);
            sw.Stop();
            return new ResultadoEnvio(Sucesso: true, ProviderUsado: "meta:template", DuracaoMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Falha Meta WhatsApp template {Template} para {Destinatario}", templateName, destino);
            return new ResultadoEnvio(Sucesso: false, ProviderUsado: "meta:template",
                ErroDetalhado: ex.Message, DuracaoMs: sw.ElapsedMilliseconds);
        }
    }
}
