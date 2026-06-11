namespace EasyStock.Application.UseCases.MenuFavoritos;

public sealed record ObterFavoritosMenuQuery(Guid UsuarioId, Guid EmpresaId, Guid LojaId);

/// <summary>
/// Resposta do GET de favoritos (ADR-0032 P1-1). <see cref="Favoritos"/> null = sem linha
/// (o front aplica o seed por perfil); lista (possivelmente vazia) = personalizado.
/// <see cref="KdsHabilitado"/> vem da ConfiguracaoLoja, p/ o front semear sem hop extra.
/// </summary>
public sealed record FavoritosMenuResult(IReadOnlyList<string>? Favoritos, bool KdsHabilitado);

public class ObterFavoritosMenuUseCase(
    ILojaRepository lojaRepository,
    IConfiguracaoLojaRepository configuracaoRepository,
    IPreferenciaMenuRepository preferenciaRepository)
{
    public async Task<FavoritosMenuResult> ExecuteAsync(ObterFavoritosMenuQuery query)
    {
        // Valida loja ∈ empresa (a empresa vem da claim no controller) — fecha IDOR.
        var loja = await lojaRepository.GetByIdAsync(query.EmpresaId, query.LojaId)
            ?? throw new UseCaseValidationException("Loja nao encontrada.");

        var config = await configuracaoRepository.GetOrDefaultAsync(loja.Id);
        var pref = await preferenciaRepository.GetAsync(query.UsuarioId, loja.Id);

        return new FavoritosMenuResult(pref?.FavoritosMenu, config.KdsHabilitado);
    }
}
