using EasyStock.Application.Services.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#2895: o roteiro padrão manda o Assistente falar como a loja, não como "o assistente da loja".</summary>
public class RoteiroAtendimentoVozTests
{
    [Fact]
    public void AcolherApresentaALojaENaoOAssistente()
    {
        var acolher = RoteiroAtendimento.Padrao.Single(e => e.Nome == "Acolher").Instrucao;

        acolher.Should().NotContain("assistente da loja");
        acolher.Should().Contain("atendimento da loja");
    }
}
