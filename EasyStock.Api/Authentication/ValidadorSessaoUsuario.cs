using System.Globalization;
using System.Security.Claims;
using EasyStock.Api.Configuration;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Api.Authentication;

/// <summary>
/// Sessão revogável (#1352, N7). Confere, a cada requisição autenticada por JWT e a cada heartbeat do SSE, se o
/// token ainda vale: o usuário existe e está ativo, e o token foi emitido (<c>iat</c>) a partir do corte
/// <c>Usuario.SessoesValidasDesde</c>. O JWT leva nível e permissões e não tinha como ser cancelado antes do
/// <c>exp</c> (480 min); este validador devolve o 401 na hora de reset de senha, troca de senha, desativação e
/// troca de perfil.
/// <list type="bullet">
///   <item>Cache de 60 s por usuário (<see cref="CacheKeys.Sessao"/>), com o valor <see cref="SessaoDoUsuario"/>.
///   No miss lê a projeção leve <see cref="IUsuarioRepository.ObterSessaoAsync"/>: <c>usuarios</c> não tem
///   <c>EmpresaId</c>, então não há bypass de RLS. O <c>RevogadorSessoes</c> apaga a chave: no mesmo processo o
///   efeito é imediato; entre réplicas, imediato com Redis e em até 60 s sem Redis.</item>
///   <item>Comparação em segundos inteiros: <c>iat &gt;= corte</c> vale, então o token emitido no mesmo segundo
///   da revogação passa (janela de 1 s, aceita e documentada).</item>
///   <item>Falha o token sem <c>sub</c> ou <c>iat</c>, de usuário inexistente ou inativo.</item>
///   <item>Cache fora do ar vira miss: a autenticação não depende do Redis.</item>
///   <item>A chave <c>Auth:SessoesRevogaveis</c> (padrão <c>true</c>) desliga a checagem sem migração.</item>
/// </list>
/// </summary>
public sealed class ValidadorSessaoUsuario(
    ICacheService cache,
    IUsuarioRepository usuarios,
    IConfiguration configuration,
    ILogger<ValidadorSessaoUsuario> logger)
{
    /// <summary>A sessão do <paramref name="principal"/> (claims do JWT já validado) ainda vale?</summary>
    public async Task<bool> ValidarAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!Habilitado()) return true;

        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var usuarioId) || usuarioId == Guid.Empty)
            return false;
        if (!long.TryParse(principal.FindFirstValue("iat"), NumberStyles.None, CultureInfo.InvariantCulture, out var emitidoEm))
            return false;

        var sessao = await ObterSessaoAsync(usuarioId);
        if (!sessao.Ativo) return false;

        return sessao.SessoesValidasDesde is not { } corte || emitidoEm >= SegundosUnix(corte);
    }

    /// <summary>Padrão <c>true</c>. Valor ausente ou que não é booleano não desliga a segurança por engano.</summary>
    private bool Habilitado() =>
        !bool.TryParse(configuration[ConfigurationKeys.AuthSessoesRevogaveis], out var revogaveis) || revogaveis;

    private async Task<SessaoDoUsuario> ObterSessaoAsync(Guid usuarioId)
    {
        var chave = CacheKeys.Sessao(usuarioId);

        try
        {
            if (await cache.GetAsync<SessaoDoUsuario>(chave) is { } emCache)
                return emCache;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ValidadorSessaoUsuario: cache indisponivel na leitura, seguindo pelo banco");
        }

        // Usuário que não existe fica em cache como inativo: token de conta apagada não bate no banco a cada pedido.
        var sessao = await usuarios.ObterSessaoAsync(usuarioId) ?? new SessaoDoUsuario(Ativo: false, SessoesValidasDesde: null);

        try
        {
            await cache.SetAsync(chave, sessao, CacheKeys.SessaoTtl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ValidadorSessaoUsuario: cache indisponivel na gravacao");
        }

        return sessao;
    }

    private static long SegundosUnix(DateTime corte) =>
        new DateTimeOffset(corte.Kind == DateTimeKind.Local ? corte.ToUniversalTime() : DateTime.SpecifyKind(corte, DateTimeKind.Utc))
            .ToUnixTimeSeconds();
}
