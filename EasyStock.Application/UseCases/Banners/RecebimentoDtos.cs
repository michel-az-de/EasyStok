namespace EasyStock.Application.UseCases.Banners;

/// <summary>
/// Projeção do console de recebimento de um aviso (#875) para o Admin. E-mail já vem
/// MASCARADO (minimização LGPD); a revelação do completo é endpoint separado e auditado.
/// </summary>
public sealed record BannerRecebimentoDto(
    Guid BannerId,
    string TituloInterno,
    bool ExigeConfirmacao,
    int Elegiveis,
    int Viram,
    int Confirmaram,
    IReadOnlyList<RecebimentoSerieDia> Serie,
    IReadOnlyList<RecebimentoEventoDto> Eventos,
    int TotalEventos,
    int Page,
    int PageSize);

/// <summary>Ponto da linha do tempo — total de interações no dia (BRT, chave "yyyy-MM-dd").</summary>
public sealed record RecebimentoSerieDia(string Dia, int Total);

/// <summary>Linha do log de recebimento. E-mail mascarado; revelação sob clique (auditada).</summary>
public sealed record RecebimentoEventoDto(
    Guid UsuarioId,
    string Nome,
    string EmailMascarado,
    string Tipo,
    DateTime RegistradoEmUtc);

// ── Read model bruto na fronteira do IBannerRecebimentoQuery ─────────────────
// E-mail vem cru do banco; o use case mascara antes de sair da Application.
public sealed record BannerRecebimentoReadModel(
    string TituloInterno,
    bool ExigeConfirmacao,
    int Elegiveis,
    int Viram,
    int Confirmaram,
    IReadOnlyList<RecebimentoSerieDia> Serie,
    IReadOnlyList<RecebimentoEventoRaw> Eventos,
    int TotalEventos);

public sealed record RecebimentoEventoRaw(
    Guid UsuarioId,
    string Nome,
    string Email,
    string Tipo,
    DateTime RegistradoEmUtc);
