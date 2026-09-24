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
}
