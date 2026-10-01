using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

public class PromptAtendimentoTests
{
    [Fact]
    public void RefleteTomENivel()
    {
        var discreto = ConfiguracaoAtendimento.CriarPadrao(Guid.NewGuid());
        discreto.Atualizar("acolhedor e caseiro", NivelSugestaoAtendimento.Discreto,
            null, null, null, null, null, null, null);

        var ativo = ConfiguracaoAtendimento.CriarPadrao(Guid.NewGuid());
        ativo.Atualizar("direto e objetivo", NivelSugestaoAtendimento.Ativo,
            null, null, null, null, null, null, null);

        var promptDiscreto = PromptAtendimento.Montar(discreto);
        var promptAtivo = PromptAtendimento.Montar(ativo);

        promptDiscreto.Should().Contain("acolhedor e caseiro").And.Contain("Discreto");
        promptAtivo.Should().Contain("direto e objetivo").And.Contain("Ativo");
        promptDiscreto.Should().NotBe(promptAtivo);
    }

    [Fact]
    public void ContemRegrasRN01aRN08()
    {
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(Guid.Empty);

        var prompt = PromptAtendimento.Montar(configuracao);

        for (var i = 1; i <= 8; i++)
            prompt.Should().Contain($"RN-0{i}");
        prompt.Should().Contain("D3").And.Contain(configuracao.Tom).And.Contain("[interno]");
        prompt.Should().Contain("Para fechar um pedido use criar_pedido"); // S11: pedido e link pela conversa

        // Snapshot: mudança no system prompt é deliberada e revisada no diff deste arquivo.
        var snapshot = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Services", "Atendimento", "PromptAtendimento.snapshot.txt"));
        Normalizar(prompt).Should().Be(Normalizar(snapshot));
    }

    [Fact]
    public void Alergia_SoComAFicha_NuncaGaranteAusencia()
    {
        var prompt = PromptAtendimento.Montar(ConfiguracaoAtendimento.CriarPadrao(Guid.Empty));

        // #1314: alergia responde só pela ficha; ausência de alérgeno nunca é garantida pelo agente.
        prompt.Should().Contain("Alergia ou restrição alimentar")
            .And.Contain("responda só com os campos alergenos e ingredientes que consultar_cardapio devolver")
            .And.Contain("nunca garanta que o item não contém")
            .And.Contain("use escalar_para_dona");
    }

    private static string Normalizar(string texto) => texto.Replace("\r\n", "\n").Trim();
}
