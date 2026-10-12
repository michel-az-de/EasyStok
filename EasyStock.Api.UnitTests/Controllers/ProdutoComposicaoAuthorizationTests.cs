using System.Reflection;
using EasyStock.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// Trava a policy de escrita da receita (issue #1462). A receita define custo e a baixa
/// de insumo da producao, entao so gestao altera. A classe so tem <c>[Authorize]</c>
/// e a API nao tem FallbackPolicy: sem a policy no metodo, ate Visualizador reescrevia.
/// </summary>
public class ProdutoComposicaoAuthorizationTests
{
    [Fact]
    public void SubstituirReceita_ExigeGerente()
    {
        var metodo = typeof(ProdutoComposicaoController)
            .GetMethod(nameof(ProdutoComposicaoController.Substituir))!;

        metodo.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy)
            .Should().Contain("Gerente", "alterar receita e decisao de gestao");
    }
}
