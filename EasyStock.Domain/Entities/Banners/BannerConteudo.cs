namespace EasyStock.Domain.Entities.Banners;

/// <summary>
/// Dados editáveis de um <see cref="Banner"/> — usados tanto na criação quanto na
/// atualização, para que ambas revalidem as MESMAS invariantes (evita o PUT virar
/// porta dos fundos). Campos de ciclo de vida (Id, auditoria, NotificadoEm) não
/// entram aqui: são geridos pela própria entidade.
/// </summary>
public sealed record BannerConteudo
{
    /// <summary>Rótulo interno no console Admin (não aparece no app Web).</summary>
    public required string TituloInterno { get; init; }

    public required BannerTipo Tipo { get; init; }

    /// <summary>Mensagem do modal quando <see cref="Tipo"/> = Mensagem.</summary>
    public string? Corpo { get; init; }

    /// <summary>Chave no <c>IFileStorage</c> (nunca base64). Obrigatória quando Tipo = Imagem.</summary>
    public string? ImagemStorageKey { get; init; }

    /// <summary>URL pública permanente da imagem devolvida pelo upload.</summary>
    public string? ImagemUrl { get; init; }

    /// <summary>Link clicável on/off. Só permitido em banner de imagem.</summary>
    public bool LinkAtivo { get; init; }

    /// <summary>URL de destino do link. Deve ser absoluta http/https quando <see cref="LinkAtivo"/>.</summary>
    public string? LinkUrl { get; init; }

    /// <summary>Abre o link em nova aba (target=_blank + rel=noopener).</summary>
    public bool NovaAba { get; init; }

    /// <summary>Tooltip on/off. Só permitido em banner de imagem.</summary>
    public bool TooltipAtivo { get; init; }

    public string? TooltipTexto { get; init; }

    public BannerTamanhoModo TamanhoModo { get; init; } = BannerTamanhoModo.HerdadoDaImagem;

    public int? LarguraPx { get; init; }

    public int? AlturaPx { get; init; }

    /// <summary>Aparece uma vez por usuário (auto-registra Visto no primeiro render).</summary>
    public bool VisualizacaoUnica { get; init; }

    /// <summary>Exige "Ok, recebi". Apresentado como modal; precede <see cref="VisualizacaoUnica"/>.</summary>
    public bool ExigeConfirmacao { get; init; }

    /// <summary>Também enfileira uma notificação na primeira ativação (ADR-0030).</summary>
    public bool NotificarAoPublicar { get; init; }

    /// <summary>Ativo/visível. Default false (safe) — o Admin liga explicitamente.</summary>
    public bool Ativo { get; init; }

    /// <summary>Início da janela de exibição (UTC). Null = vale desde já.</summary>
    public DateTime? InicioEm { get; init; }

    /// <summary>Fim da janela de exibição (UTC). Null = sem expiração.</summary>
    public DateTime? FimEm { get; init; }

    /// <summary>Ordem na fila (maior aparece primeiro).</summary>
    public int Prioridade { get; init; }
}
