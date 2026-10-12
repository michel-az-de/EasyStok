namespace EasyStock.Application.Services.Auth;

/// <summary>
/// Derruba as sessões de um usuário na hora (#1352, N7). Faz três coisas, nesta ordem:
/// <list type="number">
///   <item>grava o corte <c>Usuario.SessoesValidasDesde</c>: todo JWT emitido antes dele deixa de valer;</item>
///   <item>revoga os refresh tokens ativos do usuário num UPDATE só;</item>
///   <item>apaga a chave do cache do validador (<see cref="CacheKeys.Sessao"/>), por último, para que um miss
///   já leia o valor gravado.</item>
/// </list>
/// Quem chama (reset de senha, troca de senha, desativação, troca de perfil e, adiante, N8 e N9) segue com o
/// próprio <c>CommitAsync</c>; este serviço não comita. Os dois UPDATEs são imediatos, então chame antes do
/// commit: o pior caso é o usuário deslogado sem a troca ter acontecido, nunca a troca sem o logout.
/// <para>
/// O login nunca chama este serviço: ele revoga só o refresh dos outros aparelhos, e logar o tablet não pode
/// derrubar o balcão.
/// </para>
/// </summary>
public sealed class RevogadorSessoes(
    IUsuarioRepository usuarios,
    IRefreshTokenRepository refreshTokens,
    ICacheService cache,
    TimeProvider relogio,
    ILogger<RevogadorSessoes> logger)
{
    /// <summary>Revoga as sessões de <paramref name="usuario"/> e devolve quantos refresh tokens ativos caíram.</summary>
    public async Task<int> RevogarAsync(Usuario usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        var agora = relogio.GetUtcNow().UtcDateTime;
        usuario.RevogarSessoes(agora);

        await usuarios.AtualizarSessoesValidasDesdeAsync(usuario.Id, usuario.SessoesValidasDesde!.Value);
        var refreshRevogados = await refreshTokens.RevogarSessoesAtivasAsync(usuario.Id, agora);
        await ApagarCacheAsync(usuario.Id);

        return refreshRevogados;
    }

    private async Task ApagarCacheAsync(Guid usuarioId)
    {
        try
        {
            await cache.RemoveAsync(CacheKeys.Sessao(usuarioId));
        }
        catch (Exception ex)
        {
            // O corte já está gravado e o cache é só otimização: sem a limpeza, a entrada expira sozinha no TTL.
            // Cache fora do ar não pode impedir a troca de senha nem a desativação.
            logger.LogWarning(ex,
                "RevogadorSessoes: nao consegui apagar a chave de sessao do usuario {UsuarioId}; vale o TTL de {TtlSegundos}s",
                usuarioId, CacheKeys.SessaoTtl.TotalSeconds);
        }
    }
}
