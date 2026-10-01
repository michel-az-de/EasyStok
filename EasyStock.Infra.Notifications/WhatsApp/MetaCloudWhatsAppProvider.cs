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
/// <item>conversa aberta dentro da janela: texto renderizado, ou mensagem interativa quando há
/// <c>Metadados["botao1..3"]</c> no formato <c>id|título</c> (S26);</item>
/// <item>sem conversa aberta ou fora da janela, com <c>Metadados["template"]</c>: template com <c>param1..N</c>
/// e, com <c>Metadados["imagem"]</c> (arte da campanha, #1226), a imagem no cabeçalho;</item>
/// <item>dentro da janela com <c>Metadados["imagem"]</c>: imagem com o texto como legenda;</item>
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

    /// <summary>Limite da Meta para a legenda de uma imagem.</summary>
    public const int LegendaImagemTamanhoMaximo = 1024;

    private const string IdiomaPadrao = "pt_BR";

    public string Nome => "meta";

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var template = LerTemplate(mensagem.Metadados);
        var botoes = LerBotoes(mensagem.Metadados);
        var imagem = LerImagem(mensagem.Metadados);

        try
        {
            var agora = DateTime.UtcNow;
            var contato = Conversa.NormalizarContato(CanalConversa.WhatsApp, mensagem.Destinatario);
            var conversa = await ObterConversaAbertaAsync(mensagem.EmpresaId, contato, ct);
            var dentroDaJanela = conversa?.DentroDaJanela(agora) == true;

            if (conversa is not null && !dentroDaJanela && template is null)
                return Falha(ErroForaDaJanelaSemTemplate, permanente: true, sw);

            var canal = resolvedorCanal.Obter(CanalConversa.WhatsApp);
            string wamid;
            if (template is { } t && !dentroDaJanela)
                wamid = botoes.Count > 0
                    ? await canal.EnviarModeloAsync(contato, t.Nome, t.Idioma, t.Parametros, botoes, ct)
                    : imagem is not null
                        ? await canal.EnviarModeloComImagemAsync(contato, t.Nome, t.Idioma, t.Parametros, imagem, ct)
                        : await canal.EnviarModeloAsync(contato, t.Nome, t.Idioma, t.Parametros, ct);
            else if (botoes.Count > 0 && dentroDaJanela)
                wamid = await canal.EnviarBotoesAsync(contato, mensagem.Corpo, botoes, ct);
            else if (imagem is not null)
                wamid = await EnviarImagemComTextoAsync(canal, contato, imagem, mensagem.Corpo, ct);
            else
                wamid = await canal.EnviarTextoAsync(contato, mensagem.Corpo, ct);

            if (conversa is not null)
                await RegistrarNoHistoricoSemReenvioAsync(conversa, mensagem, wamid, agora, ct);

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
            // Nome do template pode vir do diagnóstico do admin: sem quebra de linha no log (log forging).
            logger.LogError(ex, "Falha Meta WhatsApp template {Template}.",
                templateName.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal));
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

    /// <summary>
    /// #1290: a Meta já entregou. Falha ao gravar a cópia no histórico não pode voltar como falha do envio,
    /// senão o outbox reenvia a mesma mensagem ao cliente. Loga e descarta o pendente: o dispatcher grava
    /// o resultado do outbox no mesmo escopo, e o insert inválido derrubaria o commit dele também.
    /// </summary>
    private async Task RegistrarNoHistoricoSemReenvioAsync(
        Conversa conversa, MensagemPronta mensagem, string wamid, DateTime agora, CancellationToken ct)
    {
        try
        {
            await RegistrarNoHistoricoAsync(conversa, mensagem.Corpo, wamid, agora, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Meta WhatsApp: enviado (wamid {Wamid}), mas a cópia no histórico da conversa {ConversaId} falhou (outbox {OutboxId}).",
                wamid, conversa.Id, mensagem.OutboxId);
            unitOfWork.DescartarAlteracoesPendentes();
        }
    }

    private async Task RegistrarNoHistoricoAsync(Conversa conversa, string texto, string wamid, DateTime agora, CancellationToken ct)
    {
        var copia = Mensagem.Saida(conversa.EmpresaId, conversa.Id, AutorMensagem.Sistema, agora,
            TipoConteudoMensagem.Texto, Truncar(texto), wamid);
        conversa.RegistrarSaida(agora);
        await conversaRepository.AddMensagemAsync(copia, ct);
        await unitOfWork.CommitAsync();
    }

    /// <summary>
    /// Imagem com o texto como legenda (#1226: arte da campanha dentro da janela). Legenda acima do limite
    /// da Meta sai em duas mensagens: a imagem sem legenda e depois o texto, cujo id fica no histórico.
    /// </summary>
    private static async Task<string> EnviarImagemComTextoAsync(
        ICanalMensageria canal, string contato, string imagem, string texto, CancellationToken ct)
    {
        if (texto.Length <= LegendaImagemTamanhoMaximo)
            return await canal.EnviarImagemAsync(contato, imagem, texto, ct);

        await canal.EnviarImagemAsync(contato, imagem, null, ct);
        return await canal.EnviarTextoAsync(contato, texto, ct);
    }

    /// <summary><c>imagem</c>: URL HTTPS pública (cabeçalho do template ou imagem com legenda na janela).</summary>
    private static string? LerImagem(IReadOnlyDictionary<string, string>? metadados) =>
        metadados is not null && metadados.TryGetValue("imagem", out var url) && !string.IsNullOrWhiteSpace(url)
            ? url.Trim()
            : null;

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

    /// <summary><c>botao1..3</c> no formato <c>id|título</c>; entradas vazias ou sem título são ignoradas.</summary>
    private static IReadOnlyList<(string Id, string Titulo)> LerBotoes(IReadOnlyDictionary<string, string>? metadados)
    {
        var botoes = new List<(string Id, string Titulo)>();
        if (metadados is null) return botoes;

        for (var n = 1; n <= 3; n++)
        {
            if (!metadados.TryGetValue($"botao{n}", out var valor) || string.IsNullOrWhiteSpace(valor)) continue;
            var separador = valor.LastIndexOf('|');
            if (separador <= 0 || separador == valor.Length - 1) continue;
            botoes.Add((valor[..separador].Trim(), valor[(separador + 1)..].Trim()));
        }

        return botoes;
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
