namespace EasyStock.Application.UseCases.Banners;

// ── Admin: listagem paginada ────────────────────────────────────────────────
public sealed record ListarBannersAdminQuery(bool? Ativo, int Page, int PageSize);

public sealed record ListarBannersAdminResult(IReadOnlyList<BannerAdminDto> Itens, int Total, int Page, int PageSize);

public sealed class ListarBannersAdminUseCase(IBannerRepository repo)
{
    public async Task<ListarBannersAdminResult> ExecuteAsync(ListarBannersAdminQuery q, CancellationToken ct = default)
    {
        var (itens, total) = await repo.ListarAdminAsync(q.Ativo, q.Page, q.PageSize, ct);
        return new ListarBannersAdminResult(itens.Select(BannerAdminDto.De).ToList(), total, q.Page, q.PageSize);
    }
}

// ── Admin: detalhe ──────────────────────────────────────────────────────────
public sealed record ObterBannerQuery(Guid Id);

public sealed class ObterBannerUseCase(IBannerRepository repo)
{
    public async Task<BannerAdminDto> ExecuteAsync(ObterBannerQuery q, CancellationToken ct = default)
    {
        var banner = await repo.ObterAsync(q.Id, ct)
            ?? throw new BannerNaoEncontradoException();
        return BannerAdminDto.De(banner);
    }
}

// ── Web: banners ativos ainda não confirmados/vistos pelo usuário ───────────
public sealed record ListarBannersAtivosQuery(Guid UsuarioId);

public sealed class ListarBannersAtivosUseCase(IBannerRepository repo)
{
    public async Task<IReadOnlyList<BannerWebDto>> ExecuteAsync(ListarBannersAtivosQuery q, CancellationToken ct = default)
    {
        var ativos = await repo.ListarAtivosNaoConfirmadosAsync(q.UsuarioId, DateTime.UtcNow, ct);
        return ativos.Select(BannerWebDto.De).ToList();
    }
}
