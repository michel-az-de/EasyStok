using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>
/// N1, quarentena por prazo: backlog velho não se envia. Cada tipo de evento tem um prazo de validade; o aviso ao cliente
/// (S13) e a segurança envelhecem mais rápido do que o padrão.
/// </summary>
public class PoliticaValidadeNotificacaoTests
{
    private static PoliticaValidadeNotificacao NovaPolitica(Dictionary<string, int>? sobrescritas = null) =>
        new(Options.Create(new QuarentenaNotificacaoOptions
        {
            Prazos = new Dictionary<string, int>(sobrescritas ?? [], StringComparer.OrdinalIgnoreCase)
        }));

    [Fact]
    public void Todo_tipo_tem_prazo_e_o_aviso_ao_cliente_e_mais_curto_que_o_padrao()
    {
        var politica = NovaPolitica();

        foreach (var tipo in Enum.GetValues<TipoEventoNotificacao>())
            politica.PrazoDe(tipo).Should().BeGreaterThan(TimeSpan.Zero, $"{tipo} precisa de prazo");

        TipoEventoNotificacao[] avisosAoCliente =
        [
            TipoEventoNotificacao.PedidoPagoConfirmado, TipoEventoNotificacao.PedidoEmPreparo,
            TipoEventoNotificacao.PedidoSaiuParaEntrega, TipoEventoNotificacao.PedidoEntregue,
            TipoEventoNotificacao.AvaliacaoSolicitada, TipoEventoNotificacao.ReembolsoEfetuado
        ];
        foreach (var tipo in avisosAoCliente)
            politica.PrazoDe(tipo).Should().Be(TimeSpan.FromHours(2)).And.BeLessThan(PoliticaValidadeNotificacao.PrazoPadrao, $"{tipo}");
    }

    [Theory]
    [InlineData(TipoEventoNotificacao.ResetSenha, 30)]
    [InlineData(TipoEventoNotificacao.ConfirmacaoEmail, 30)]
    [InlineData(TipoEventoNotificacao.CampanhaMarketing, 60)]
    [InlineData(TipoEventoNotificacao.CampanhaLembreteEncerramento, 60)]
    [InlineData(TipoEventoNotificacao.ConversaEscalada, 60)]
    [InlineData(TipoEventoNotificacao.LembreteVencido, 60)]
    [InlineData(TipoEventoNotificacao.ProdutoVencendo, 24 * 60)]
    [InlineData(TipoEventoNotificacao.FaturaVencida, 24 * 60)]
    public void Prazos_iniciais_da_spec(TipoEventoNotificacao tipo, int minutos) =>
        NovaPolitica().PrazoDe(tipo).Should().Be(TimeSpan.FromMinutes(minutos));

    [Theory]
    [InlineData(TipoEventoNotificacao.IncidenteSistema, 2 * 60)]
    [InlineData(TipoEventoNotificacao.PrazoEstourado, 6 * 60)]
    [InlineData(TipoEventoNotificacao.ResumoDiario, 4 * 60)]
    [InlineData(TipoEventoNotificacao.ConviteAcesso, 24 * 60)]
    public void TiposDoCatalogoTemPrazoProprio(TipoEventoNotificacao tipo, int minutos) =>
        NovaPolitica().PrazoDe(tipo).Should().Be(TimeSpan.FromMinutes(minutos));

    [Fact]
    public void Configuracao_sobrescreve_o_prazo_do_tipo_em_minutos_e_ignora_valor_invalido()
    {
        var politica = NovaPolitica(new()
        {
            ["ResetSenha"] = 15,
            ["pedidoentregue"] = 90,          // o nome do tipo não diferencia maiúsculas
            ["ProdutoVencendo"] = 0,          // inválido: fica o padrão
            ["TipoQueNaoExiste"] = 10,        // desconhecido: ignorado
        });

        politica.PrazoDe(TipoEventoNotificacao.ResetSenha).Should().Be(TimeSpan.FromMinutes(15));
        politica.PrazoDe(TipoEventoNotificacao.PedidoEntregue).Should().Be(TimeSpan.FromMinutes(90));
        politica.PrazoDe(TipoEventoNotificacao.ProdutoVencendo).Should().Be(PoliticaValidadeNotificacao.PrazoPadrao);
    }

    [Fact]
    public void PorPrazo_agrupa_todos_os_tipos_sem_repetir_nem_perder_nenhum()
    {
        var grupos = NovaPolitica().PorPrazo();

        grupos.SelectMany(g => g.Tipos).Should().BeEquivalentTo(Enum.GetValues<TipoEventoNotificacao>());
        grupos.Select(g => g.Prazo).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(TipoEventoNotificacao.ResetSenha, true)]
    [InlineData(TipoEventoNotificacao.ConfirmacaoEmail, true)]
    [InlineData(TipoEventoNotificacao.PedidoEntregue, false)]
    public void CarregaSegredo_so_para_os_tipos_de_seguranca(TipoEventoNotificacao tipo, bool esperado) =>
        PoliticaValidadeNotificacao.CarregaSegredo(tipo).Should().Be(esperado);
}
