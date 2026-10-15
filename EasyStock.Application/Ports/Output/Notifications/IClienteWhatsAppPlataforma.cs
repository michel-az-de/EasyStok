namespace EasyStock.Application.Ports.Output.Notifications;

/// <summary>
/// Versão da Graph API usada nas chamadas à Meta (N6). Um ponto só: as duas options (cliente e provider) nascem com
/// ela e a <c>BaseUrl</c> deriva dela. A Meta aposenta uma versão a cada ~2 anos; trocar é configuração
/// (<c>Notifications:WhatsApp:Meta:ApiVersion</c>), não código. A v25.0 é o plano B (vale até 29/07/2028).
/// </summary>
public static class VersaoGraphApi
{
    public const string Padrao = "v26.0";

    public static string BaseUrlDe(string? versao) =>
        $"https://graph.facebook.com/{(string.IsNullOrWhiteSpace(versao) ? Padrao : versao.Trim())}";
}

/// <summary>
/// Como a Meta trata um código de erro (N6), pela tabela de <see cref="CodigosErroMeta"/>. Uma regra só para a loja e
/// para a plataforma.
/// </summary>
public enum ClasseErroMeta
{
    /// <summary>Nunca vai passar: <c>Falhado</c> sem retentativa.</summary>
    Permanente = 1,

    /// <summary>Permanente que também é problema de operação (token, pagamento, conta, número): contador e log de erro.</summary>
    PermanenteComAlerta = 2,

    /// <summary>Pode passar: o dispatcher reagenda pelo recuo. Inclui código fora da tabela.</summary>
    Transitorio = 3
}

/// <summary>Tabela de códigos de erro da Meta (doc oficial de códigos de erro, lida em 01/10/2026).</summary>
public static class CodigosErroMeta
{
    private static readonly HashSet<int> Permanentes =
        [100, 131008, 131009, 131021, 131026, 131030, 131047, 131049, 131050, 131051, 132000, 132001, 132005, 132007, 132012, 132015, 132016, 132018];

    private static readonly HashSet<int> PermanentesComAlerta =
        [0, 3, 10, 190, 131005, 131042, 131031, 368, 130497, 131037, 131045, 133010, 131048];

    public static ClasseErroMeta Classificar(int codigo) =>
        Permanentes.Contains(codigo) ? ClasseErroMeta.Permanente
        : PermanentesComAlerta.Contains(codigo) ? ClasseErroMeta.PermanenteComAlerta
        : ClasseErroMeta.Transitorio;

    public static bool EhPermanente(int codigo) => Classificar(codigo) != ClasseErroMeta.Transitorio;
}

/// <summary>
/// Template para o número de plataforma. <paramref name="BotaoUrl0"/> e <paramref name="BotaoUrl1"/> são o valor do
/// parâmetro do botão <c>url</c> (índices 0 e 1). <paramref name="CopyCode"/>: template de autenticação, em que o
/// código vai no corpo e no botão e tem até 15 caracteres. <paramref name="OpacoCallback"/> volta no webhook de status
/// (<c>biz_opaque_callback_data</c>), só com GUIDs.
/// </summary>
public sealed record EnvioTemplatePlataforma(
    string Para,
    string Nome,
    string Idioma,
    IReadOnlyList<string> ParametrosCorpo,
    string? BotaoUrl0,
    string? BotaoUrl1,
    bool CopyCode,
    string OpacoCallback);

/// <summary>
/// Desfecho do envio pelo número de plataforma, sem exceção (N6). <see cref="Desfecho"/> é
/// <c>Enviado</c> (com <paramref name="Wamid"/>), <c>FalhaPermanente</c>, <c>FalhaTransitoria</c> (nada saiu) ou
/// <c>Indeterminado</c> (a Meta pode ter aceitado).
/// </summary>
public sealed record ResultadoEnvioPlataforma(
    DesfechoEnvio Desfecho,
    string? Wamid = null,
    int? CodigoMeta = null,
    ClasseErroMeta? Classe = null,
    int? StatusHttp = null,
    string? Erro = null);

/// <summary>
/// Porta do WhatsApp de plataforma (N6): o 2º número da WABA, com <c>phone_number_id</c> explícito, sem tenant e sem
/// <c>Conversa</c>. Namespace de Notifications de propósito: nada aqui depende do atendimento.
/// </summary>
public interface IClienteWhatsAppPlataforma
{
    Task<ResultadoEnvioPlataforma> EnviarTemplatePlataformaAsync(EnvioTemplatePlataforma envio, CancellationToken ct = default);

    /// <summary>Texto livre, só dentro da janela de serviço (a resposta automática de quem escreveu ao número).</summary>
    Task<ResultadoEnvioPlataforma> EnviarTextoPlataformaAsync(string waId, string texto, CancellationToken ct = default);
}
