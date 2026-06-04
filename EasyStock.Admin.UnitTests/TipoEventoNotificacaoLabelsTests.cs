using EasyStock.Admin.Pages.Notificacoes;
using FluentAssertions;

namespace EasyStock.Admin.UnitTests;

/// <summary>
/// BUG-008 (#463): os dropdowns de Templates geram opcoes de
/// <see cref="TipoEventoNotificacaoLabels"/> (fonte unica, humanizada). Trava a
/// contagem (drift do filtro hardcoded que listava 9 de 36) e a humanizacao.
/// </summary>
public class TipoEventoNotificacaoLabelsTests
{
    [Theory]
    [InlineData("FaturaPaga", "Fatura paga")]
    [InlineData("ProdutoVencendo", "Produto vencendo")]
    [InlineData("ResetSenha", "Reset senha")]
    [InlineData("BroadcastSuperAdmin", "Broadcast super admin")]
    [InlineData("PedidoAgendadoEm1Hora", "Pedido agendado em 1 hora")]
    [InlineData("PedidoAgendadoEm10Minutos", "Pedido agendado em 10 minutos")]
    [InlineData("SlaProximoVencer", "Sla proximo vencer")]
    public void Humanizar_quebra_PascalCase_em_frase(string entrada, string esperado)
        => TipoEventoNotificacaoLabels.Humanizar(entrada).Should().Be(esperado);

    [Fact]
    public void Humanizar_e_robusto_para_vazio_e_nulo()
    {
        TipoEventoNotificacaoLabels.Humanizar("").Should().Be("");
        TipoEventoNotificacaoLabels.Humanizar(null!).Should().BeNull();
    }

    [Fact]
    public void Opcoes_cobre_os_36_eventos_com_rotulos_validos_e_valores_unicos()
    {
        TipoEventoNotificacaoLabels.Opcoes.Should().HaveCount(36);
        TipoEventoNotificacaoLabels.Opcoes.Should()
            .OnlyContain(o => !string.IsNullOrWhiteSpace(o.Valor) && !string.IsNullOrWhiteSpace(o.Rotulo));
        TipoEventoNotificacaoLabels.Opcoes.Select(o => o.Valor).Should().OnlyHaveUniqueItems();
    }
}
