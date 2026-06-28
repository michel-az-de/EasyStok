using EasyStock.Domain.Sales;
using EasyStock.Web.Helpers;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Helpers;

/// <summary>
/// Poka-yoke anti-drift (issue #719). O EasyStock.Web (BFF) espelha o vocabulário de
/// status do domínio em <see cref="StatusHelper"/>. Quando alguém adiciona um
/// <see cref="StatusPedido"/> novo e esquece de mapear no Web, o status vaza CRU, sem
/// badge nem rótulo — foi exatamente o BUG-003/014/015 do QA v1.10 (storefront emitia
/// <c>aguardando_aprovacao_baba</c> e o Web não conhecia).
///
/// Este teste itera o enum canônico e falha se qualquer valor não tiver entrada em
/// <see cref="StatusHelper"/> — transformando o próximo drift em build vermelho na CI,
/// não em badge torta em produção. (Os arch tests Category=Architecture são cegos a isso.)
/// </summary>
public class StatusHelperStatusVocabularyTests
{
    public static IEnumerable<object[]> TodosOsStatusPedido()
        => Enum.GetValues<StatusPedido>().Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(TodosOsStatusPedido))]
    public void StatusHelper_cobre_todo_StatusPedido_do_dominio(StatusPedido status)
    {
        // StatusPedidoMapper.Format é a fonte canônica da string de fio (lança se o
        // enum crescer sem ser mapeado lá também — cobre os dois lados do contrato).
        var wire = StatusPedidoMapper.Format(status);

        var info = StatusHelper.Resolve(wire);

        // Falta de mapeamento se manifesta de dois jeitos conforme a build:
        //  - Release: o fallback devolve a própria string crua como Label (Label == wire).
        //  - Debug:   o fallback grita "UNMAPPED:{status}".
        // Cobrimos os dois pra o gate valer em qualquer configuração.
        info.Label.Should().NotBe(wire,
            $"o status '{wire}' ({status}) precisa de entrada em StatusHelper.Map (issue #719)");
        info.Label.Should().NotStartWith("UNMAPPED:",
            $"o status '{wire}' ({status}) caiu no fallback de dev — falta no StatusHelper.Map (issue #719)");
    }
}
