using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#2544 (S71, D1 a): o primeiro contato cumprimenta e deixa o Assistente conduzir; o link não vai junto.</summary>
public class SaudacaoAtendimentoTests
{
    private const string Link = "https://loja.exemplo/cardapio";

    [Fact]
    public void PrimeiroContato_Padrao_SaiSemLink()
    {
        var texto = SaudacaoAtendimento.Montar(ConfiguracaoAtendimento.CriarPadrao(Guid.Empty), null, Link);

        texto.Should().NotContain(Link).And.NotContain("{link}").And.NotContain("cardápio");
    }

    [Fact]
    public void Retorno_Padrao_SaiPeloNomeESemLink()
    {
        var texto = SaudacaoAtendimento.Montar(ConfiguracaoAtendimento.CriarPadrao(Guid.Empty), "Maria", Link);

        texto.Should().StartWith("Oi, Maria!").And.NotContain(Link).And.NotContain("{link}");
    }

    [Fact]
    public void TextoDaLojaComLink_RecebeOLink()
    {
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(Guid.Empty);
        configuracao.Atualizar(null, null, "Oi! Nosso cardápio: {link}", null, null, null, null, null, null);

        SaudacaoAtendimento.Montar(configuracao, null, Link).Should().StartWith($"Oi! Nosso cardápio: {Link}");
    }

    [Fact]
    public void TextoDaLojaSemLink_NaoGanhaLinkAnexado()
    {
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(Guid.Empty);
        configuracao.Atualizar(null, null, "Oi! Como posso ajudar?", null, null, null, null, null, null);

        SaudacaoAtendimento.Montar(configuracao, null, Link).Should().NotContain(Link);
    }
}
