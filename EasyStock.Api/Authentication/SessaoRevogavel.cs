using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace EasyStock.Api.Authentication;

/// <summary>
/// Gancho do JwtBearer para a sessão revogável (#1352): depois que assinatura, emissor, audiência e <c>exp</c>
/// passaram, o <see cref="ValidadorSessaoUsuario"/> confere o corte de sessão do usuário. Falhou, o pedido é 401.
/// Fica fora do <c>AddEasyStockAuth</c> para o teste de pipeline usar exatamente o mesmo código de produção.
/// </summary>
public static class SessaoRevogavel
{
    public static async Task AoValidarTokenAsync(TokenValidatedContext contexto)
    {
        var validador = contexto.HttpContext.RequestServices.GetRequiredService<ValidadorSessaoUsuario>();

        if (contexto.Principal is null || !await validador.ValidarAsync(contexto.Principal))
            contexto.Fail("Sessão revogada.");
    }
}
