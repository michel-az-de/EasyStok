using System.Reflection;
using EasyStock.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// N0 (#1349): trava a superfície anônima do <see cref="AuthController"/>. Rota anônima é porta aberta à
/// internet: <c>POST register</c> criava conta sem empresa, mandava e-mail a qualquer endereço e
/// enumerava contas, e foi removida na N0. A lista é fechada: rota anônima nova exige editar
/// <see cref="RotasAnonimasConhecidas"/>, e a mudança aparece no diff da revisão
/// (molde: <see cref="DiagnosticoAuthorizationTests"/>).
/// </summary>
public class AuthControllerRotasTests
{
    /// <summary>
    /// "VERBO template" de cada ação sem <c>[Authorize]</c>. Vale como anônima porque a API não tem
    /// <c>FallbackPolicy</c>: sem o atributo, ninguém exige login.
    /// </summary>
    private static readonly string[] RotasAnonimasConhecidas =
    [
        "POST login",
        "GET google/config",
        "POST google/login",
        "POST lista-empresas",
        "POST refresh",
        "POST logout",
        "POST forgot-password",
        "POST reset-password",
        "POST reset-password-code",
        "POST confirmar-email",
    ];

    [Fact]
    public void Superficie_anonima_do_AuthController_e_a_lista_conhecida()
    {
        RotasAnonimas(typeof(AuthController)).Should().BeEquivalentTo(
            RotasAnonimasConhecidas,
            "a superficie anonima do AuthController e fechada: rota anonima nova exige editar a lista, "
            + "e o register anonimo foi removido na N0 (#1349)");
    }

    /// <summary>Ação sem <c>[Authorize]</c> (no método, na classe ou nas bases) ou com <c>[AllowAnonymous]</c>.</summary>
    private static IEnumerable<string> RotasAnonimas(Type controller) =>
        from acao in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        where EhAnonima(acao)
        from rota in acao.GetCustomAttributes(inherit: true).OfType<IRouteTemplateProvider>()
        let verbos = rota is IActionHttpMethodProvider http ? string.Join("|", http.HttpMethods) : "ANY"
        select $"{verbos} {rota.Template}";

    private static bool EhAnonima(MethodInfo acao) =>
        acao.IsDefined(typeof(AllowAnonymousAttribute), inherit: true)
        || !(acao.IsDefined(typeof(AuthorizeAttribute), inherit: true)
             || acao.DeclaringType!.IsDefined(typeof(AuthorizeAttribute), inherit: true));
}
