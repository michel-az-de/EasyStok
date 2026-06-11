namespace EasyStock.Application.UseCases.MenuFavoritos;

public sealed record SalvarFavoritosMenuCommand(
    Guid UsuarioId, Guid EmpresaId, Guid LojaId, IReadOnlyList<string> Favoritos);

/// <summary>
/// Upsert dos favoritos do menu (ADR-0032 fatia 4). Saneia a lista (trim, dedup,
/// cap 20, descarta vazios/longos) e devolve a lista normalizada p/ a UI otimista
/// reconciliar. Chaves desconhecidas NAO sao validadas aqui (o builder do menu, no
/// Web, descarta orfaos no render) — a Application nao conhece o MenuDefinition.
/// Segue a convencao do ConfiguracaoLoja (sem catch de EF): o indice unico
/// (UsuarioId, LojaId) garante integridade; a corrida rara de 1o-pin concorrente
/// surge como erro, nao merge silencioso.
/// </summary>
public class SalvarFavoritosMenuUseCase(
    ILojaRepository lojaRepository,
    IPreferenciaMenuRepository preferenciaRepository,
    IUnitOfWork unitOfWork,
    ILogger<SalvarFavoritosMenuUseCase> logger)
{
    public const int MaxFavoritos = 20;
    public const int MaxTamanhoChave = 64;

    public async Task<IReadOnlyList<string>> ExecuteAsync(SalvarFavoritosMenuCommand command)
    {
        var loja = await lojaRepository.GetByIdAsync(command.EmpresaId, command.LojaId)
            ?? throw new UseCaseValidationException("Loja nao encontrada.");

        var favoritos = Sanitizar(command.Favoritos);

        var pref = await preferenciaRepository.GetAsync(command.UsuarioId, loja.Id);
        if (pref is null)
        {
            pref = PreferenciaMenuUsuario.Criar(command.UsuarioId, loja.Id, command.EmpresaId, favoritos);
            await preferenciaRepository.AddAsync(pref);
        }
        else
        {
            pref.DefinirFavoritos(favoritos);
            await preferenciaRepository.UpdateAsync(pref);
        }

        await unitOfWork.CommitAsync();
        logger.LogInformation(
            "Favoritos do menu salvos: usuario {UsuarioId} loja {LojaId} ({N} itens).",
            command.UsuarioId, loja.Id, favoritos.Count);

        return favoritos;
    }

    private static IReadOnlyList<string> Sanitizar(IReadOnlyList<string>? entrada)
    {
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        var saida = new List<string>();
        foreach (var raw in entrada ?? Array.Empty<string>())
        {
            if (saida.Count >= MaxFavoritos) break;
            var k = raw?.Trim();
            if (string.IsNullOrEmpty(k) || k.Length > MaxTamanhoChave) continue;
            if (vistos.Add(k)) saida.Add(k);
        }
        return saida;
    }
}
