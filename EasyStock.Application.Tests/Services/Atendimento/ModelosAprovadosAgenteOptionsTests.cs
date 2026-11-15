using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Atendimento;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// S68 (#2597, ADR-0066): a loja escolhe o modelo só dentro de <c>Anthropic:ModelosAprovados</c>. A lista nasce com o
/// modelo atual (<c>Anthropic:ModeloAgente</c>), sempre em primeiro.
/// </summary>
public class ModelosAprovadosAgenteOptionsTests
{
    private static ModelosAprovadosAgenteOptions Ler(Dictionary<string, string?> valores)
    {
        var lista = new ModelosAprovadosAgenteOptions();
        lista.Ler(new ConfigurationBuilder().AddInMemoryCollection(valores).Build());
        return lista;
    }

    [Fact]
    public void ListaNasceComOModeloAtualEmPrimeiro()
    {
        var lista = Ler(new()
        {
            ["Anthropic:ModeloAgente"] = "accounts/fireworks/models/deepseek-v4p1-flash",
            ["Anthropic:ModelosAprovados:0:Id"] = "claude-haiku-5",
            ["Anthropic:ModelosAprovados:0:Rotulo"] = "Econômico",
            ["Anthropic:ModelosAprovados:0:CustoRelativo"] = "econômico",
        });

        lista.Modelos.Select(m => m.Id).Should().Equal("accounts/fireworks/models/deepseek-v4p1-flash", "claude-haiku-5");
        lista.Modelos[0].CustoRelativo.Should().Be("padrão");
        lista.Modelos[1].Should().Be(new ModeloAprovadoAgente("claude-haiku-5", "Econômico", "econômico"));
    }

    [Fact]
    public void ModeloAtualJaListadoSobeParaOComecoSemRepetir()
    {
        var lista = Ler(new()
        {
            ["Anthropic:ModeloAgente"] = "claude-sonnet-5",
            ["Anthropic:ModelosAprovados:0:Id"] = "claude-haiku-5",
            ["Anthropic:ModelosAprovados:1:Id"] = "claude-sonnet-5",
            ["Anthropic:ModelosAprovados:1:Rotulo"] = "Equilibrado",
            ["Anthropic:ModelosAprovados:2:Id"] = "claude-haiku-5",
            ["Anthropic:ModelosAprovados:3:Rotulo"] = "sem id",
        });

        lista.Modelos.Select(m => m.Id).Should().Equal("claude-sonnet-5", "claude-haiku-5");
        lista.Modelos[0].Rotulo.Should().Be("Equilibrado");
        lista.Modelos[1].Rotulo.Should().Be("claude-haiku-5", "sem rótulo, o id serve de rótulo");
    }

    [Fact]
    public void SoOModeloDaListaVaiParaOTurno()
    {
        var lista = Ler(new() { ["Anthropic:ModeloAgente"] = "claude-sonnet-5" });

        lista.Aprovado("claude-sonnet-5").Should().BeTrue();
        lista.Aprovado("claude-opus-5").Should().BeFalse();
        lista.Aprovado(null).Should().BeFalse();
        lista.DoTurno("claude-sonnet-5").Should().Be("claude-sonnet-5");
        lista.DoTurno("claude-opus-5").Should().BeNull();
        lista.DoTurno(null).Should().BeNull();
    }

    /// <summary>Revisão da #2618: sem <c>Anthropic:ModeloAgente</c>, o cliente usa o padrão, e a lista também.</summary>
    [Fact]
    public void SemAChaveDoModeloAtualAListaNasceComOPadrao() =>
        Ler([]).Modelos.Select(m => m.Id).Should().Equal(RequisicaoLlm.ModeloPadrao);
}
