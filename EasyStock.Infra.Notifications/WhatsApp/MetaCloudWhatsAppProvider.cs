using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Notifications.WhatsApp;

/// <summary>
/// Provider da Meta no outbox de notificações (S09). Envia pela porta de canal (S34,
/// <see cref="ResolvedorCanal"/> com <see cref="CanalConversa.WhatsApp"/>) e decide o formato pela
/// janela de 24 h da conversa aberta do destinatário:
/// <list type="bullet">
/// <item>conversa aberta dentro da janela: texto renderizado;</item>
/// <item>sem conversa aberta ou fora da janela, com <c>Metadados["template"]</c>: template com <c>param1..N</c>;</item>
/// <item>conversa aberta fora da janela sem template: falha permanente, sem chamar a Meta;</item>
/// <item>sem conversa e sem template: tenta o texto; se a Meta recusar com 131047, falha permanente.</item>
/// </list>
/// Com conversa aberta, a saída é copiada para o histórico como <c>Mensagem(Saida, Sistema)</c> com o <c>wamid</c>.
/// </summary>
public sealed class MetaCloudWhatsAppProvider(
    ResolvedorCanal resolvedorCanal,
    IConversaRepository conversaRepository,
    ITenantContextAccessor tenantContext,
    IUnitOfWork unitOfWork,
    ILogger<MetaCloudWhatsAppProvider> logger) : IProvedorWhatsApp
{
    public const string ErroForaDaJanelaSemTemplate = "fora_da_janela_24h_sem_template";

    private const string IdiomaPadrao = "pt_BR";

    public string Nome => "meta";

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var template = LerTemplate(mensagem.Metadados);

        try
        {
            var agora = DateTime.UtcNow;
            var contato = Conversa.NormalizarContato(CanalConversa.WhatsApp, mensagem.Destinatario);
            var conversa = await ObterConversaAbertaAsync(mensagem.EmpresaId, contato, ct);
            var dentroDaJanela = conversa?.DentroDaJanela(agora) == true;

            if (conversa is not null && !dentroDaJanela && template is null)
                return Falha(ErroForaDaJanelaSemTemplate, permanente: true, sw);

            var canal = resolvedorCanal.Obter(CanalConversa.WhatsApp);
            var wamid = template is { } t && !dentroDaJanela
                ? await canal.EnviarModeloAsync(contato, t.Nome, t.Idioma, t.Parametros, ct)
                : await canal.EnviarTextoAsync(contato, mensagem.Corpo, ct);

            if (conversa is not null)
                await RegistrarNoHistoricoAsync(conversa, mensagem.Corpo, wamid, agora, ct);

            sw.Stop();
            return new ResultadoEnvio(Sucesso: true, ProviderUsado: Nome, DuracaoMs: sw.ElapsedMilliseconds);
        }
        catch (WhatsAppCloudException ex) when (ex.Codigo == WhatsAppCloudException.CodigoForaDaJanela && template is null)
        {
            logger.LogWarning("Meta recusou texto fora da janela de 24 h (outbox {OutboxId}).", mensagem.OutboxId);
            return Falha(ErroForaDaJanelaSemTemplate, permanente: true, sw);
        }
        catch (WhatsAppCloudException ex)
        {
            logger.LogError(ex, "Falha Meta WhatsApp {Codigo} (outbox {OutboxId}).", ex.Codigo, mensagem.OutboxId);
            return Falha(ex.Message, permanente: ex.EhPermanente, sw);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha Meta WhatsApp (outbox {OutboxId}).", mensagem.OutboxId);
            return Falha(ex.Message, permanente: false, sw);
        }
    }

    /// <summary>
    /// Onda 2.1 — envia template aprovado na Meta, direto (diagnóstico do admin). Variáveis posicionais
    /// ({{1}}, {{2}}, ...) na ordem do template aprovado; <paramref name="languageCode"/> no padrão da Meta (pt_BR).
    /// </summary>
    public async Task<ResultadoEnvio> EnviarTemplateAsync(
        string destino,
        string templateName,
        IReadOnlyList<string> vars,
        string languageCode = IdiomaPadrao,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await resolvedorCanal.Obter(CanalConversa.WhatsApp).EnviarModeloAsync(destino, templateName, languageCode, vars, ct);
            sw.Stop();
            return new ResultadoEnvio(Sucesso: true, ProviderUsado: "meta:template", DuracaoMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            logger.LogError(ex, "Falha Meta WhatsApp template {Template}.", templateName);
            return new ResultadoEnvio(Sucesso: false, ProviderUsado: "meta:template",
                ErroDetalhado: ex.Message, DuracaoMs: sw.ElapsedMilliseconds);
        }
    }

    private async Task<Conversa?> ObterConversaAbertaAsync(Guid empresaId, string contato, CancellationToken ct)
    {
        // Diagnóstico do admin envia sem empresa: não há conversa para consultar.
        if (empresaId == Guid.Empty) return null;

        // O dispatcher roda sem claim JWT: sem o tenant, o filtro global e a RLS zeram a busca.
        tenantContext.SetCurrentTenant(empresaId);
        return await conversaRepository.ObterAbertaPorContatoAsync(empresaId, CanalConversa.WhatsApp, contato, ct);
    }

    private async Task RegistrarNoHistoricoAsync(Conversa conversa, string texto, string wamid, DateTime agora, CancellationToken ct)
    {
        var copia = Mensagem.Saida(conversa.EmpresaId, conversa.Id, AutorMensagem.Sistema, agora,
            TipoConteudoMensagem.Texto, Truncar(texto), wamid);
        conversa.RegistrarSaida(agora);
        await conversaRepository.AddMensagemAsync(copia, ct);
        await unitOfWork.CommitAsync();
    }

    private static (string Nome, string Idioma, IReadOnlyList<string> Parametros)? LerTemplate(
        IReadOnlyDictionary<string, string>? metadados)
    {
        if (metadados is null || !metadados.TryGetValue("template", out var nome) || string.IsNullOrWhiteSpace(nome))
            return null;

        var idioma = metadados.TryGetValue("idioma", out var i) && !string.IsNullOrWhiteSpace(i) ? i.Trim() : IdiomaPadrao;
        var parametros = new List<string>();
        for (var n = 1; metadados.TryGetValue($"param{n}", out var valor); n++)
            parametros.Add(valor);

        return (nome.Trim(), idioma, parametros);
    }

    private static string Truncar(string texto) =>
        texto.Length <= Mensagem.TextoTamanhoMaximo ? texto : texto[..Mensagem.TextoTamanhoMaximo];

    private ResultadoEnvio Falha(string erro, bool permanente, System.Diagnostics.Stopwatch sw)
    {
        sw.Stop();
        return new ResultadoEnvio(Sucesso: false, ProviderUsado: Nome, ErroDetalhado: erro,
            DuracaoMs: sw.ElapsedMilliseconds, FalhaPermanente: permanente);
    }
}
