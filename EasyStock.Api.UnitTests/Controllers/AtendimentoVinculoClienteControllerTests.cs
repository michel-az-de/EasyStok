using System.Reflection;
using EasyStock.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>#2447: o telefone da busca de candidatos vai no corpo, fora da URL que o proxy grava no log.</summary>
public class AtendimentoVinculoClienteControllerTests
{
    [Fact]
    public void Candidatos_por_POST_recebe_o_telefone_no_corpo()
    {
        var acao = typeof(AtendimentoVinculoClienteController).GetMethods()
            .SingleOrDefault(m => m.GetCustomAttribute<HttpPostAttribute>()?.Template == "candidatos-cliente");

        acao.Should().NotBeNull("o console busca candidatos por POST para não pôr o telefone na URL");
        acao!.GetParameters().Should().Contain(p => p.GetCustomAttribute<FromBodyAttribute>() != null
            && p.ParameterType.GetProperty("Telefone") != null);
        acao.GetParameters().Should().NotContain(p => p.GetCustomAttribute<FromQueryAttribute>() != null);
    }
}
