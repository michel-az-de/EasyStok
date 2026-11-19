using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>
/// #2895: texto padrão ao cliente segue a voz da marca (quem fala é a Casa, sem emoji no lugar de texto,
/// sem primeira pessoa, despedida "Boa massa!").
/// </summary>
public class VozDosTextosPadraoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 11, 19, 15, 0, 0, DateTimeKind.Utc);

    private static readonly string[] ForaDaVoz =
    [
        "bem-vindo(a)", "Obrigada pela preferência", "Até a próxima", "me chama", "te ajudo", "posso te",
        "estamos", "fechamos", "voltarmos", "respondemos", "Recebemos",
    ];

    public static TheoryData<string, string> TextosPadrao()
    {
        var configuracao = new ConfiguracaoAtendimento();
        var expediente = ExpedienteLoja.CriarPadrao(Empresa);
        return new TheoryData<string, string>
        {
            { "saudação de primeiro contato", ConfiguracaoAtendimento.SaudacaoPrimeiroContatoPadrao },
            { "saudação de retorno", ConfiguracaoAtendimento.SaudacaoRetornoPadrao },
            { "frase de espera", configuracao.FraseEspera },
            { "fora do horário", expediente.MensagemForaDoHorario },
            { "loja fechada", expediente.MensagemLojaFechada },
            { "pagamento confirmado", RegraAutomatica.TextoPadraoPagamentoConfirmado },
            { "cobrança vencendo", RegraAutomatica.TextoPadraoCobrancaVencendo },
            { "saudação solta", RegraAutomatica.TextoPadraoSaudacaoSolta },
        };
    }

    [Theory]
    [MemberData(nameof(TextosPadrao))]
    public void TextoPadraoFalaComoACasa(string qual, string texto)
    {
        foreach (var trecho in ForaDaVoz)
            texto.Should().NotContainEquivalentOf(trecho, $"{qual} sai em nome da Casa");
        texto.Any(c => char.IsSurrogate(c) || c > 0x2000).Should().BeFalse($"{qual} não usa emoji no lugar de texto");
    }

    [Fact]
    public void PagamentoConfirmadoMantemAsVariaveisDoDisparo()
    {
        RegraAutomatica.TextoPadraoPagamentoConfirmado.Should().Contain("{nome}").And.Contain("{faixa}");
        RegraAutomatica.TextoPadraoCobrancaVencendo.Should().Contain("{pedido}").And.NotContain("{nome}");
        RegraAutomatica.Padrao(Empresa, GatilhoAutomacao.SaudacaoSolta, Agora)!.Ligada.Should().BeFalse();
    }
}
