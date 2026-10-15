using System.Security.Claims;

namespace EasyStock.Api.Authentication;

/// <summary>
/// Leitura do usuário logado para exibição. O JWT usa <c>NameClaimType = "sub"</c>, então
/// <c>Identity.Name</c> devolve o id; o nome legível vem da claim "nome" (#1474).
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>Claim "nome", depois "email"; nulo quando nenhuma das duas está preenchida.</summary>
    public static string? NomeExibicao(this ClaimsPrincipal? usuario)
    {
        if (usuario is null) return null;

        foreach (var tipo in new[] { "nome", "email", ClaimTypes.Email })
        {
            var valor = usuario.FindFirstValue(tipo);
            if (!string.IsNullOrWhiteSpace(valor)) return valor;
        }

        return null;
    }
}
