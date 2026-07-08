namespace EasyStock.Application.UseCases.Banners;

public sealed record ConsultarRecebimentoBannerQuery(
    Guid BannerId, int Page, int PageSize, string? Tipo, string? Busca);

/// <summary>
/// Monta o console de recebimento de um aviso (#875) para o Admin: resumo, série diária
/// e log paginado. Mascara o e-mail de cada linha (minimização LGPD) — o completo só sai
/// pelo <see cref="RevelarEmailRecebimentoUseCase"/>, sob demanda e com auditoria.
/// </summary>
public sealed class ConsultarRecebimentoBannerUseCase(IBannerRecebimentoQuery query)
{
    public async Task<BannerRecebimentoDto> ExecuteAsync(ConsultarRecebimentoBannerQuery q, CancellationToken ct = default)
    {
        var page = Math.Max(1, q.Page);
        var pageSize = Math.Clamp(q.PageSize, 1, 100);

        var rm = await query.ObterAsync(q.BannerId, page, pageSize, q.Tipo, q.Busca, ct)
            ?? throw new BannerNaoEncontradoException();

        var eventos = rm.Eventos
            .Select(e => new RecebimentoEventoDto(e.UsuarioId, e.Nome, e.Empresa, MascararEmail(e.Email), e.Tipo, e.RegistradoEmUtc))
            .ToList();

        return new BannerRecebimentoDto(
            q.BannerId, rm.TituloInterno, rm.ExigeConfirmacao,
            rm.Elegiveis, rm.Viram, rm.Confirmaram, rm.Serie, eventos, rm.TotalEventos, page, pageSize);
    }

    /// <summary>Minimização LGPD: "f****o@dominio.com". O e-mail completo só sai pelo endpoint auditado.</summary>
    public static string MascararEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "-";
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        var local = email[..at];
        var dominio = email[at..];
        if (local.Length <= 2) return $"{local[..1]}***{dominio}";
        var meio = new string('*', Math.Min(local.Length - 2, 5));
        return $"{local[0]}{meio}{local[^1]}{dominio}";
    }
}

/// <summary>
/// Revela o e-mail completo de um usuário sob demanda no console de recebimento, deixando
/// trilha de auditoria (LGPD Art. 37 — registro das operações). Só o SuperAdmin chega aqui.
/// </summary>
public sealed class RevelarEmailRecebimentoUseCase(
    IBannerRecebimentoQuery query, ILogger<RevelarEmailRecebimentoUseCase> logger)
{
    public async Task<string?> ExecuteAsync(Guid usuarioId, Guid solicitanteId, CancellationToken ct = default)
    {
        var email = await query.ObterEmailUsuarioAsync(usuarioId, ct);
        if (email is not null)
            logger.LogInformation(
                "LGPD/recebimento: SuperAdmin {Solicitante} revelou e-mail do usuario {Alvo} em {Quando:o}.",
                solicitanteId, usuarioId, DateTime.UtcNow);
        return email;
    }
}
