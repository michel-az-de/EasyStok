using System.Globalization;

namespace EasyStock.Application.Tests.UseCases.Common;

public class CulturaTests
{
    // QA v1.10 r3 BUG-008 / issue 610: a Api nao fixa a cultura do thread, entao um
    // ":C" sem cultura caia em Invariant e renderizava o simbolo de moeda generico (¤)
    // nos Eventos do pedido. Cultura.PtBr formata em R$ independente da cultura corrente.
    [Fact]
    public void PtBr_formata_moeda_em_real_mesmo_sob_cultura_invariant()
    {
        var anterior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var texto = 1019m.ToString("C", Cultura.PtBr);

            texto.Should().StartWith("R$");
            texto.Should().NotContain("¤"); // ¤ = simbolo de moeda generico da cultura Invariant
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }
}
