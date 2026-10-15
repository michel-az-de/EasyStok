using System.Diagnostics;
using System.Diagnostics.Metrics;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Notifications.WhatsApp.Plataforma;

/// <summary>
/// WhatsApp de plataforma (N6, ADR-0057): o 2º número da WABA, só template, nunca toca a <c>Conversa</c> nem o tenant.
/// Depende apenas de <see cref="IClienteWhatsAppPlataforma"/> e do estado global dos templates; a fronteira com o
/// atendimento é guardada por <c>PlataformaNaoDependeDoAtendimentoTests</c>.
/// <para>
/// No máximo uma vez: o cliente devolve o desfecho sem lançar e <c>Indeterminado</c> (sem resposta ou 5xx) é terminal,
/// sem reenvio. Recusas da Meta se classificam pela tabela de <see cref="CodigosErroMeta"/>; as de operação (token,
/// pagamento, conta, número) também sobem como contador e log de erro.
/// </para>
/// </summary>
public sealed class MetaCloudWhatsAppPlataformaProvider(
    IClienteWhatsAppPlataforma cliente,
    ITemplateMetaEstadoRepository estados,
    ILogger<MetaCloudWhatsAppPlataformaProvider> logger) : IProvedorWhatsApp
{
    public const string ErroTemplateObrigatorio = "template_obrigatorio";
    public const string ErroMarketing = "marketing_nao_sai_pela_plataforma";
    public const string ErroRecategorizado = "template_recategorizado_marketing";

    private const string IdiomaPadrao = "pt_BR";

    private static readonly Meter Medidor = new("EasyStock.Notifications", "1.0");

    private static readonly Counter<long> Alertas = Medidor.CreateCounter<long>(
        "notifications.whatsapp.plataforma.alerta", "alertas",
        "Recusas da Meta no número de plataforma que são problema de operação (token, pagamento, conta, número)");

    public string Nome => "meta-plataforma";

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var metadados = mensagem.Metadados;
        if (metadados is null || !metadados.TryGetValue("template", out var nome) || string.IsNullOrWhiteSpace(nome))
            return Permanente(ErroTemplateObrigatorio, sw);

        if (mensagem.Categoria == CategoriaConteudoNotificacao.Marketing)
            return Permanente(ErroMarketing, sw);

        nome = nome.Trim();
        var idioma = metadados.TryGetValue("idioma", out var i) && !string.IsNullOrWhiteSpace(i) ? i.Trim() : IdiomaPadrao;

        var estado = await estados.ObterAsync(nome, idioma, ct);
        if (estado is { EhMarketing: true })
            return Permanente(ErroRecategorizado, sw);

        var parametros = new List<string>();
        for (var n = 1; metadados.TryGetValue($"param{n}", out var valor); n++)
            parametros.Add(valor);

        var envio = new EnvioTemplatePlataforma(
            mensagem.Destinatario,
            nome,
            idioma,
            parametros,
            Ler(metadados, "botaoUrl0"),
            Ler(metadados, "botaoUrl1"),
            CopyCode: mensagem.Categoria == CategoriaConteudoNotificacao.Seguranca,
            OpacoCallback: $"{mensagem.EmpresaId:N}.{mensagem.OutboxId:N}");

        var r = await cliente.EnviarTemplatePlataformaAsync(envio, ct);
        sw.Stop();

        if (r.Classe == ClasseErroMeta.PermanenteComAlerta)
        {
            Alertas.Add(1, new TagList { { "codigo", r.CodigoMeta ?? 0 } });
            // Sem telefone no log (LGPD, #1292); o OutboxId leva à mensagem.
            logger.LogError(
                "WhatsApp de plataforma: Meta recusou com {Codigo}, problema de operação (token, pagamento, conta ou número) outbox={OutboxId}",
                r.CodigoMeta, mensagem.OutboxId);
        }

        return r.Desfecho switch
        {
            DesfechoEnvio.Enviado => new ResultadoEnvio(true, Nome, DuracaoMs: sw.ElapsedMilliseconds, StatusHttp: r.StatusHttp)
            {
                IdExterno = r.Wamid
            },
            DesfechoEnvio.Indeterminado => ResultadoEnvio.Indeterminado(
                Nome, Descrever(r), r.StatusHttp, sw.ElapsedMilliseconds),
            DesfechoEnvio.FalhaPermanente => new ResultadoEnvio(false, Nome, Descrever(r), r.StatusHttp,
                DuracaoMs: sw.ElapsedMilliseconds, FalhaPermanente: true),
            _ => new ResultadoEnvio(false, Nome, Descrever(r), r.StatusHttp, DuracaoMs: sw.ElapsedMilliseconds)
        };
    }

    private static string? Ler(IReadOnlyDictionary<string, string> metadados, string chave) =>
        metadados.TryGetValue(chave, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static string Descrever(ResultadoEnvioPlataforma r) =>
        r.CodigoMeta is { } c ? $"meta_{c}: {r.Erro}" : r.Erro ?? "falha_sem_detalhe";

    private ResultadoEnvio Permanente(string erro, Stopwatch sw)
    {
        sw.Stop();
        return new ResultadoEnvio(false, Nome, erro, DuracaoMs: sw.ElapsedMilliseconds, FalhaPermanente: true);
    }
}
