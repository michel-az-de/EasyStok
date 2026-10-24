using EasyStock.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace EasyStock.Api.UnitTests.Authorization;

/// <summary>
/// #1265-11 / #1992 (decisão do Felipe, 23/10): respostas prontas e mensagens programadas só Gerente e dona criam,
/// editam e arquivam; a atendente usa. Mesmo padrão das automáticas (#1965).
/// </summary>
public class RespostasEProgramadasAutorizacaoTests
{
    private static string? Policy<T>(string acao) =>
        typeof(T).GetMethod(acao)!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().SingleOrDefault()?.Policy;

    private static string? PolicyDoController<T>() =>
        typeof(T).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy;

    [Theory]
    [InlineData(nameof(AtendimentoRespostasProntasController.Criar))]
    [InlineData(nameof(AtendimentoRespostasProntasController.Editar))]
    [InlineData(nameof(AtendimentoRespostasProntasController.Arquivar))]
    public void EscritaDasRespostasProntas_ExigeGerente(string acao)
    {
        Policy<AtendimentoRespostasProntasController>(acao).Should().Be("Gerente");
    }

    [Theory]
    [InlineData(nameof(AtendimentoRespostasProntasController.Listar))]
    [InlineData(nameof(AtendimentoRespostasProntasController.Renderizar))]
    public void UsoDasRespostasProntas_SegueParaQuemAtende(string acao)
    {
        PolicyDoController<AtendimentoRespostasProntasController>().Should().Be("Operador");
        Policy<AtendimentoRespostasProntasController>(acao).Should().BeNull();
    }

    [Theory]
    [InlineData(nameof(AtendimentoMensagensProgramadasController.Agendar))]
    [InlineData(nameof(AtendimentoMensagensProgramadasController.Cancelar))]
    public void EscritaDasProgramadas_ExigeGerente(string acao)
    {
        Policy<AtendimentoMensagensProgramadasController>(acao).Should().Be("Gerente");
    }

    [Fact]
    public void ListaDasProgramadas_SegueParaQuemAtende()
    {
        PolicyDoController<AtendimentoMensagensProgramadasController>().Should().Be("Operador");
        Policy<AtendimentoMensagensProgramadasController>(nameof(AtendimentoMensagensProgramadasController.Listar)).Should().BeNull();
    }
}
