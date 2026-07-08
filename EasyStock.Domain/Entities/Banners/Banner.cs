namespace EasyStock.Domain.Entities.Banners;

/// <summary>
/// Banner de plataforma — broadcast global (NÃO tem <c>EmpresaId</c>, logo é isento
/// do filtro multi-tenant e do RLS; ver EasyStockDbContext.ApplyTenantQueryFilters).
/// Cadastrado pelo SuperAdmin no console Admin e exibido a todos os usuários no app Web.
/// Invariantes validadas no Domain, tanto em <see cref="Criar"/> quanto em
/// <see cref="Atualizar"/>, para que o PUT não vire porta dos fundos.
/// </summary>
public class Banner
{
    public Guid Id { get; private set; }

    public string TituloInterno { get; private set; } = null!;
    public BannerTipo Tipo { get; private set; }
    public string? Corpo { get; private set; }

    public string? ImagemStorageKey { get; private set; }
    public string? ImagemUrl { get; private set; }

    public bool LinkAtivo { get; private set; }
    public string? LinkUrl { get; private set; }
    public bool NovaAba { get; private set; }

    public bool TooltipAtivo { get; private set; }
    public string? TooltipTexto { get; private set; }

    public BannerTamanhoModo TamanhoModo { get; private set; }
    public int? LarguraPx { get; private set; }
    public int? AlturaPx { get; private set; }

    public bool VisualizacaoUnica { get; private set; }
    public bool ExigeConfirmacao { get; private set; }

    public bool NotificarAoPublicar { get; private set; }

    /// <summary>Carimbo da (única) notificação enfileirada. Guard de idempotência (ADR-0030).</summary>
    public DateTime? NotificadoEm { get; private set; }

    public bool Ativo { get; private set; }
    public DateTime? InicioEm { get; private set; }
    public DateTime? FimEm { get; private set; }
    public int Prioridade { get; private set; }

    public Guid? CriadoPorUsuarioId { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AtualizadoEm { get; private set; }

    // EF Core ctor sem parâmetros.
    private Banner() { }

    public static Banner Criar(BannerConteudo dados, Guid? criadoPorUsuarioId = null)
    {
        Validar(dados);

        var agora = DateTime.UtcNow;
        var banner = new Banner
        {
            Id = Guid.NewGuid(),
            CriadoPorUsuarioId = criadoPorUsuarioId,
            CriadoEm = agora,
            AtualizadoEm = agora
        };
        banner.Aplicar(dados);
        return banner;
    }

    public void Atualizar(BannerConteudo dados)
    {
        Validar(dados);
        Aplicar(dados);
        AtualizadoEm = DateTime.UtcNow;
    }

    public void Ativar()
    {
        if (Ativo) return;
        Ativo = true;
        AtualizadoEm = DateTime.UtcNow;
    }

    public void Desativar()
    {
        if (!Ativo) return;
        Ativo = false;
        AtualizadoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Marca a notificação como enfileirada (guard in-memory). O guard autoritativo
    /// contra duplo-disparo é o UPDATE atômico condicional no banco (ADR-0030, Fatia 6).
    /// Idempotente: só carimba uma vez.
    /// </summary>
    public void RegistrarNotificacao(DateTime quandoUtc)
    {
        if (NotificadoEm is not null) return;
        NotificadoEm = DateTime.SpecifyKind(quandoUtc, DateTimeKind.Utc);
        AtualizadoEm = DateTime.UtcNow;
    }

    private void Aplicar(BannerConteudo d)
    {
        TituloInterno = d.TituloInterno.Trim();
        Tipo = d.Tipo;
        Corpo = Normalizar(d.Corpo);

        var temImagem = d.Tipo == BannerTipo.Imagem;
        ImagemStorageKey = temImagem ? Normalizar(d.ImagemStorageKey) : null;
        ImagemUrl = temImagem ? Normalizar(d.ImagemUrl) : null;

        LinkAtivo = d.LinkAtivo;
        LinkUrl = d.LinkAtivo ? d.LinkUrl!.Trim() : null;
        NovaAba = d.LinkAtivo && d.NovaAba;

        TooltipAtivo = d.TooltipAtivo;
        TooltipTexto = d.TooltipAtivo ? d.TooltipTexto!.Trim() : null;

        TamanhoModo = d.TamanhoModo;
        var manual = d.TamanhoModo == BannerTamanhoModo.Manual;
        LarguraPx = manual ? d.LarguraPx : null;
        AlturaPx = manual ? d.AlturaPx : null;

        VisualizacaoUnica = d.VisualizacaoUnica;
        ExigeConfirmacao = d.ExigeConfirmacao;
        NotificarAoPublicar = d.NotificarAoPublicar;
        Ativo = d.Ativo;
        InicioEm = ParaUtc(d.InicioEm);
        FimEm = ParaUtc(d.FimEm);
        Prioridade = d.Prioridade;
    }

    private static void Validar(BannerConteudo d)
    {
        if (string.IsNullOrWhiteSpace(d.TituloInterno) || d.TituloInterno.Trim().Length > 120)
            throw new RegraDeDominioVioladaException("Título interno do banner é obrigatório (1-120 caracteres).");

        if (d.Tipo == BannerTipo.Mensagem)
        {
            if (string.IsNullOrWhiteSpace(d.Corpo) || d.Corpo.Trim().Length > 4000)
                throw new RegraDeDominioVioladaException("Banner de mensagem exige um corpo de texto (1-4000 caracteres).");
        }
        else // Imagem
        {
            if (string.IsNullOrWhiteSpace(d.ImagemStorageKey))
                throw new RegraDeDominioVioladaException("Banner de imagem exige uma imagem.");
        }

        if (d.LinkAtivo)
        {
            if (d.Tipo != BannerTipo.Imagem)
                throw new RegraDeDominioVioladaException("Link clicável só é permitido em banner de imagem.");
            if (!UrlHttpAbsolutaValida(d.LinkUrl))
                throw new RegraDeDominioVioladaException("URL de link inválida: use uma URL absoluta http ou https.");
        }

        if (d.TooltipAtivo)
        {
            if (d.Tipo != BannerTipo.Imagem)
                throw new RegraDeDominioVioladaException("Tooltip só é permitido em banner de imagem.");
            if (string.IsNullOrWhiteSpace(d.TooltipTexto) || d.TooltipTexto.Trim().Length > 300)
                throw new RegraDeDominioVioladaException("Tooltip habilitado exige um texto (1-300 caracteres).");
        }

        if (d.TamanhoModo == BannerTamanhoModo.Manual && (d.LarguraPx is not > 0 || d.AlturaPx is not > 0))
            throw new RegraDeDominioVioladaException("Tamanho manual exige largura e altura positivas.");

        if (d.InicioEm is { } inicio && d.FimEm is { } fim && fim <= inicio)
            throw new RegraDeDominioVioladaException("Janela de exibição inválida: o fim deve ser após o início.");
    }

    /// <summary>
    /// Aceita apenas URL absoluta com esquema http/https. Rejeita <c>javascript:</c>,
    /// <c>data:</c>, relativas e vazias — defesa contra XSS armazenado no href do banner.
    /// </summary>
    private static bool UrlHttpAbsolutaValida(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    /// <summary>
    /// Normaliza para UTC (Npgsql <c>timestamptz</c> exige Kind=Utc). Assume que a camada
    /// Admin já converteu BRT→UTC; <c>Unspecified</c> é tratado como já-UTC.
    /// </summary>
    private static DateTime? ParaUtc(DateTime? dt) => dt switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } => dt,
        { Kind: DateTimeKind.Local } => dt.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc)
    };
}
