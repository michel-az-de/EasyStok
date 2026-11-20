namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// #2928: de qual anúncio (Google, Meta) veio a conversa ou o pedido. Imutável; gravado como owned type em colunas
/// (o relatório filtra e agrupa por ele). Nasce só pelas fábricas, que normalizam o texto: trim, vazio vira null,
/// sem caracteres de controle, cortado no tamanho máximo. O id de clique só entra com consentimento de marketing.
/// </summary>
public sealed record OrigemAnuncio
{
    public const int TamanhoPlataforma = 20;
    public const int TamanhoVia = 20;
    public const int TamanhoTexto = 120;
    public const int TamanhoIdAnuncio = 60;
    public const int TamanhoTipoIdClique = 10;
    public const int TamanhoIdClique = 200;

    /// <summary>Por quanto tempo o id de clique é guardado; passado isso o job de poda o anula (janela de importação do Google Ads).</summary>
    public static readonly TimeSpan RetencaoIdClique = TimeSpan.FromDays(90);

    private static readonly char[] SeparadoresDaFonte = ['_', '-', '.', ' '];

    public const string PlataformaGoogle = "google";
    public const string PlataformaMeta = "meta";
    public const string PlataformaOutra = "outra";

    public const string ViaAnuncioWhatsApp = "anuncio_whatsapp";
    public const string ViaSite = "site";
    public const string ViaSiteChat = "site_chat";

    public const string TipoGclid = "gclid";
    public const string TipoGbraid = "gbraid";
    public const string TipoWbraid = "wbraid";
    public const string TipoFbclid = "fbclid";
    public const string TipoCtwaClid = "ctwa_clid";

    /// <summary>Parâmetros <c>utm_*</c> crus do site.</summary>
    public sealed record Utms(string? Fonte, string? Meio, string? Campanha, string? Conteudo, string? Termo);

    /// <summary>Ids de clique crus do site; só um é guardado, por prioridade.</summary>
    public sealed record IdsDeClique(string? Gclid, string? Gbraid, string? Wbraid, string? Fbclid);

    public string Plataforma { get; private init; } = PlataformaOutra;
    public string Via { get; private init; } = ViaSite;
    public string? Fonte { get; private init; }
    public string? Meio { get; private init; }
    public string? Campanha { get; private init; }
    public string? Conteudo { get; private init; }
    public string? Termo { get; private init; }
    public string? IdAnuncio { get; private init; }
    public string? TituloAnuncio { get; private init; }
    public string? TipoIdClique { get; private init; }
    public string? IdClique { get; private init; }
    public DateTimeOffset CapturadaEm { get; private init; }

    // EF owned type.
    private OrigemAnuncio() { }

    /// <summary>Origem vinda do site; null quando não sobra UTM nem id de clique úteis.</summary>
    public static OrigemAnuncio? DoSite(
        Utms utms, IdsDeClique idsDeClique, string via, bool consentimentoMarketing, DateTimeOffset capturadaEm)
    {
        var (tipo, id) = consentimentoMarketing ? EscolherId(idsDeClique) : (null, null);
        var fonte = Normalizar(utms.Fonte, TamanhoTexto);
        var meio = Normalizar(utms.Meio, TamanhoTexto);
        var campanha = Normalizar(utms.Campanha, TamanhoTexto);
        var conteudo = Normalizar(utms.Conteudo, TamanhoTexto);
        var termo = Normalizar(utms.Termo, TamanhoTexto);

        if (id is null && fonte is null && meio is null && campanha is null && conteudo is null && termo is null)
            return null;

        return new OrigemAnuncio
        {
            Plataforma = DerivarPlataforma(tipo, fonte),
            Via = Normalizar(via, TamanhoVia) ?? ViaSite,
            Fonte = fonte,
            Meio = meio,
            Campanha = campanha,
            Conteudo = conteudo,
            Termo = termo,
            TipoIdClique = tipo,
            IdClique = id,
            CapturadaEm = capturadaEm.ToUniversalTime(), // timestamptz no Npgsql exige offset zero
        };
    }

    /// <summary>Origem de anúncio Click-to-WhatsApp (<c>referral</c> do webhook).</summary>
    public static OrigemAnuncio DoAnuncioWhatsApp(
        string? sourceId, string? headline, string? ctwaClid, DateTimeOffset capturadaEm)
    {
        var id = Normalizar(ctwaClid, TamanhoIdClique);
        return new OrigemAnuncio
        {
            Plataforma = PlataformaMeta,
            Via = ViaAnuncioWhatsApp,
            IdAnuncio = Normalizar(sourceId, TamanhoIdAnuncio),
            TituloAnuncio = Normalizar(headline, TamanhoTexto),
            TipoIdClique = id is null ? null : TipoCtwaClid,
            IdClique = id,
            CapturadaEm = capturadaEm.ToUniversalTime(), // timestamptz no Npgsql exige offset zero
        };
    }

    /// <summary>Cópia sem o id de clique (poda de 90 dias): mantém campanha e plataforma.</summary>
    public OrigemAnuncio SemIdClique() => this with { IdClique = null, TipoIdClique = null };

    private static (string? Tipo, string? Id) EscolherId(IdsDeClique ids)
    {
        (string Tipo, string? Valor)[] ordem =
        [
            (TipoGclid, ids.Gclid), (TipoGbraid, ids.Gbraid), (TipoWbraid, ids.Wbraid), (TipoFbclid, ids.Fbclid),
        ];
        foreach (var (tipo, valor) in ordem)
        {
            var v = Normalizar(valor, TamanhoIdClique);
            if (v is not null) return (tipo, v);
        }
        return (null, null);
    }

    private static string DerivarPlataforma(string? tipo, string? fonte)
    {
        switch (tipo)
        {
            case TipoGclid or TipoGbraid or TipoWbraid: return PlataformaGoogle;
            case TipoFbclid: return PlataformaMeta;
        }
        var f = fonte?.ToLowerInvariant();
        if (f is null) return PlataformaOutra;
        if (f.Contains("google")) return PlataformaGoogle;
        // "meta" só como termo inteiro (#2986): "metabase" ou "metas_novembro" não são a Meta.
        if (f.Contains("facebook") || f.Contains("instagram") || f is "fb" or "ig"
            || f.Split(SeparadoresDaFonte).Contains("meta"))
            return PlataformaMeta;
        return PlataformaOutra;
    }

    private static string? Normalizar(string? valor, int max)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var limpo = new string(valor.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (limpo.Length == 0) return null;
        if (limpo.Length <= max) return limpo;
        // Não parte um par substituto (emoji): meio par vira UTF-8 inválido e o Npgsql recusa o INSERT.
        var corte = char.IsHighSurrogate(limpo[max - 1]) ? max - 1 : max;
        return limpo[..corte].TrimEnd();
    }
}
