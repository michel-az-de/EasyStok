using EasyStock.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace EasyStock.Api.UnitTests.Authorization;

/// <summary>
/// #1265-11: a mensagem automática vai para todos os clientes. Quem atende lê as regras; trocar ou desligar é de Admin ou
/// Gerente, como o cardápio do atendimento e o expediente.
/// </summary>
public class AutomacoesAtendimentoAutorizacaoTests
{
    private static string? Policy(string acao) =>
        typeof(AtendimentoAutomacoesController).GetMethod(acao)!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().SingleOrDefault()?.Policy;

    private static string? PolicyDoController() =>
        typeof(AtendimentoAutomacoesController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().Policy;

    [Fact]
    public void SalvarAutomacao_ExigeGerente()
    {
        Policy(nameof(AtendimentoAutomacoesController.Salvar)).Should().Be("Gerente");
    }

    [Fact]
    public void ListarAutomacoes_SegueParaQuemAtende()
    {
        PolicyDoController().Should().Be("Operador");
        Policy(nameof(AtendimentoAutomacoesController.Listar)).Should().BeNull();
    }
}
